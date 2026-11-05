using System;
using System.IO;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>Read-only checks; no assets, scenes, or temporary GameObjects are created.</summary>
public static class ContentVariantCreatorChecks
{
	[MenuItem("Tools/Art Content Tool/Run M2.1 Safe Checks")]
	public static void Run()
	{
		Check(
			ContentVariantCreator.GetOutputPath(0, " sample ") == "Assets/Game/GeneratedContent/Type_0/Content_sample.prefab",
			"ID trimming / type zero");
		Check(ContentVariantCreator.GetOutputPath(-2, "sample").Contains("Type_-2/"), "integer type folder");
		foreach (var id in new[]
		{
			"",
			" ",
			"../bad",
			"a/b",
			"a\\b",
			"a:b",
			"a?b",
			"a*b",
			"a\"b",
			"a<b",
			"a>b",
			"a|b",
			"a\nb",
			"bad."
		}

		)
		{
			Check(ContentVariantCreator.GetOutputPath(1, id) == null, "invalid ID: " + id);
		}

		Check(ContentVariantCreator.PathOccupied("Assets"), "directory collision");
		Check(ContentVariantCreator.PathOccupied("Packages/manifest.json"), "file collision");
		var errors = ContentVariantCreator.Validate(new ContentCreationDraft(), out _);
		Check(errors.Count >= 4, "missing type, ID, Template and Model");
		Check(
			ContentVariantCreator.Create(new ContentCreationDraft()).Status == VariantCreationStatus.InvalidInput,
			"invalid draft must not enter core");
		CheckCleanupFailures();
		Debug.Log("M2.1 safe checks passed. Prefab creation and post-save behavior require Unity Editor validation.");
	}

	internal static void CheckCleanupFailures()
	{
		var attempts = new List<string>();
		ContentVariantCreator.CleanupTemporaryResources(
			() => attempts.Add("template"),
			() => attempts.Add("model"),
			() => attempts.Add("scene"));
		Check(string.Join(",", attempts) == "template,model,scene", "successful cleanup order");
		// Fail each resource independently, then every combination of resources.
		for (int failureMask = 1; failureMask < 8; failureMask++)
		{
			attempts.Clear();
			var failures = new List<Exception>();
			void Cleanup(int bit, string name)
			{
				attempts.Add(name);
				if ((failureMask & bit) == 0)
				{
					return;
				}

				var failure = new IOException(name + " cleanup failure");
				failures.Add(failure);
				throw failure;
			}

			try
			{
				ContentVariantCreator.CleanupTemporaryResources(
					() => Cleanup(1, "template"),
					() => Cleanup(2, "model"),
					() => Cleanup(4, "scene"));
				Check(false, "cleanup failures must be reported");
			}
			catch (AggregateException ex)
			{
				Check(string.Join(",", attempts) == "template,model,scene", "all cleanup attempts after failure");
				Check(ex.InnerExceptions.Count == failures.Count, "all cleanup failures retained");
				for (int i = 0; i < failures.Count; i++)
				{
					Check(
						ReferenceEquals(ex.InnerExceptions[i].InnerException, failures[i]),
						"original cleanup exception retained");
				}
			}
		}

		Exception creationFailure;
		try
		{
			throw new InvalidOperationException("original creation failure");
		}
		catch (Exception ex)
		{
			creationFailure = ex;
		}

		ContentVariantCreator.CleanupTemporaryResources(() =>
		{
		}, () =>
		{
		}, () =>
		{
		}, creationFailure);
		try
		{
			ContentVariantCreator.CleanupTemporaryResources(() => throw new IOException("cleanup failure"), () =>
			{
			}, () =>
			{
			}, creationFailure);
			Check(false, "creation plus cleanup failure must be reported");
		}
		catch (AggregateException ex)
		{
			Check(
				ex.InnerExceptions.Count == 2 && ReferenceEquals(ex.InnerExceptions[0], creationFailure),
				"original creation exception is first and unchanged");
			Check(!string.IsNullOrEmpty(ex.InnerExceptions[0].StackTrace), "creation stack trace preserved");
		}

		try
		{
			ContentVariantCreator.CleanupTemporaryResources(() =>
			{
			}, () =>
			{
			}, () => throw new IOException("scene cleanup failure"), creationFailureMessage: "original save failure");
			Check(false, "failed result plus cleanup failure must be reported");
		}
		catch (AggregateException ex)
		{
			Check(
				ex.Message.Contains("original save failure") && ex.ToString().Contains("scene cleanup failure"),
				"failed result message and cleanup details retained");
		}
	}

	private static void Check(bool condition, string name)
	{
		if (!condition)
		{
			throw new InvalidOperationException("M2.1 check failed: " + name);
		}
	}
}

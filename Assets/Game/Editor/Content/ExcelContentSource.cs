using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;
using UnityEngine;

public sealed class ExcelContentSource : IContentSource
{
    private readonly string filePath;
    private readonly string sheetName;
    private Dictionary<string, int> headers = new();

    private static readonly string[] RequiredHeaders =
    {
        "Id",
        "Name",
        "PrefabAddress",
        "IconKey"
    };

    public ExcelContentSource(string filePath, string sheetName)
    {
        this.filePath = filePath;
        this.sheetName = sheetName;
    }

    public ContentLoadResult Load()
    {
        var result = new ContentLoadResult();
        bool hasMissingRequiredHeader = false;
        
        using var stream = File.OpenRead(filePath);
        IWorkbook workBook = new XSSFWorkbook(stream);

        var sheet = workBook.GetSheet(sheetName);

        if(sheet == null)
        {
            Debug.Log("no sheet");
            return result;
        }

        // row 1 = header
        headers = BuildHeaderMap(sheet.GetRow(1));

        foreach (string requiredHeader in RequiredHeaders)
        {
            if (headers.ContainsKey(requiredHeader))
            {
                continue;
            }
            else
            {   
                hasMissingRequiredHeader = true;
            }
            result.Diagnostics.Add(
                new SourceDiagnostic
                {
                    Severity = DiagnosticSeverity.Error,
                    SheetName = sheet.SheetName,
                    Row = 1,
                    Column = -1,
                    FieldName = requiredHeader,
                    Message = $"Required header '{requiredHeader}' is missing."
                }
            );
        }

        //stop parsing
        if (hasMissingRequiredHeader)
        {
            return result;
        }

        for(int rowIndex = 2; rowIndex <= sheet.LastRowNum; rowIndex++)
        {
            var row = sheet.GetRow(rowIndex);

            if(row == null)
                continue;

            var definition = new ContentDefinition
            {
                Id = ReadString(row, headers, "Id"),
                Name = ReadString(row, headers, "Name"),
                EvolutionFromId = ReadString(row, headers, "EvolutionFromId"),
                EvolutionToId = ReadString(row, headers, "EvolutionToId"),
                PrefabKey = ReadString(row, headers, "PrefabKey"),
                IconKey = ReadString(row, headers, "IconKey"),
            };

            result.Records.Add(new ContentRecord
            {
                Definition = definition,
                Source = new ContentSourceHandle
                {
                    SourceType = "Excel",
                    SourceId = $"PetSetting:{rowIndex - 1}"
                }
            });
        }

        return result;
    }

    private static string GetCellString(IRow row, int columnIndex)
    {
        var cell = row.GetCell(columnIndex);

        if(cell == null)
            return null;

        return cell.ToString()?.Trim();
    }

    private Dictionary<string, int> BuildHeaderMap(IRow headerRow)
    {
        for(int columnIndex = 0; columnIndex < headerRow.LastCellNum; columnIndex++)
        {
            var cell = headerRow.GetCell(columnIndex);

            string header = cell?.ToString()?.Trim();

            if(string.IsNullOrWhiteSpace(header))
                continue;

            headers[header] = columnIndex;
            Debug.Log(header + " = " + headers[header]);
        }

        return headers;
    }

    private string ReadString(IRow row, Dictionary<string, int> headers, string fieldName)
    {
        if(!headers.TryGetValue(fieldName, out int columnIndex))
        {
            return null;
        }

        var cell = row.GetCell(columnIndex);

        if(cell == null)
            return null;
        
        return cell.ToString()?.Trim();
    }
}
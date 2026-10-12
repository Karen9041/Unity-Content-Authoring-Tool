using System.Collections.Generic;
using System.IO;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;
using UnityEngine;

public sealed class ExcelContentSource : IContentSource
{
    private readonly string filePath;
    private readonly string sheetName;

    public ExcelContentSource(string filePath, string sheetName)
    {
        this.filePath = filePath;
        this.sheetName = sheetName;
    }

    public IReadOnlyList<ContentRecord> Load()
    {
        var results = new List<ContentRecord>();

        using var stream = File.OpenRead(filePath);
        IWorkbook workBook = new XSSFWorkbook(stream);

        var sheet = workBook.GetSheet(sheetName);

        if(sheet == null)
        {
            Debug.Log("no sheet");
            return results;
        }

        // row 1 = header
        for(int rowIndex = 2; rowIndex <= sheet.LastRowNum; rowIndex++)
        {
            var row = sheet.GetRow(rowIndex);

            if(row == null)
                continue;

            var definition = new ContentDefinition
            {
                Id = GetCellString(row, 1),
                Name = GetCellString(row, 2),
                EvolutionFromId = GetCellString(row, 3),
                EvolutionToId = GetCellString(row, 4),
                PrefabKey = GetCellString(row, 5),
                IconKey = GetCellString(row, 6)
            };

            results.Add(new ContentRecord
            {
                Definition = definition,
                Source = new ContentSourceHandle
                {
                    SourceType = "Excel",
                    SourceId = $"PetSetting:{rowIndex - 1}"
                }
            });
        }

        return results;
    }

    private static string GetCellString(IRow row, int columnIndex)
    {
        var cell = row.GetCell(columnIndex);

        if(cell == null)
            return null;

        return cell.ToString()?.Trim();
    }

}
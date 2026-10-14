using System.Collections.Generic;
using System.IO;
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

            if(TryParseRow(
                sheet,
                row,
                rowIndex,
                headers,
                result,
                out var record
            ))
            {
                result.Records.Add(record);
            }
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

    private bool TryReadString(
        IRow row, 
        Dictionary<string, int> headers, 
        string fieldName, 
        string sheetName, 
        int excelRowNumber, 
        List<SourceDiagnostic> diagnostics, 
        out string value)
    {
        value = null;

        if(!headers.TryGetValue(fieldName, out int columnIndex))
        {
            return false;
        }

        var cell = row.GetCell(columnIndex);

        if(cell == null || cell.CellType == CellType.Blank)
        {
            return true;
        }

        switch (cell.CellType)
        {
            case CellType.String:
                value = cell.StringCellValue?.Trim();
                return true;

            case CellType.Numeric:
                value = cell.NumericCellValue.ToString();
                return true;

            case CellType.Boolean:
                value = cell.BooleanCellValue.ToString();
                return true;

            case CellType.Formula:
                diagnostics.Add(new SourceDiagnostic
                {
                    Severity = DiagnosticSeverity.Error,
                    SheetName = sheetName,
                    Row = excelRowNumber,
                    Column = columnIndex + 1,
                    FieldName = fieldName,
                    Message = "Formula cells are not supported yet."
                });

                return false;

            default:
                diagnostics.Add(new SourceDiagnostic
                {
                    Severity = DiagnosticSeverity.Error,
                    SheetName = sheetName,
                    Row = excelRowNumber,
                    Column = columnIndex + 1,
                    FieldName = fieldName,
                    Message = $"Unsupported cell type: {cell.CellType}"
                });

                return false;
        }
    }

    private bool TryReadInt(
        IRow row, 
        Dictionary<string, int> headers, 
        string fieldName, 
        string sheetName, 
        int excelRowNumber, 
        List<SourceDiagnostic> diagnostics, 
        out int value
    )
    {
        value = default;

        if(!headers.TryGetValue(fieldName, out int columnIndex))
        {
            return false;
        }

        var cell = row.GetCell(columnIndex);

        if(cell == null || cell.CellType == CellType.Blank)
        {
            return true;
        }

        switch (cell.CellType)
        {
            case CellType.Numeric:
                double numericValue = cell.NumericCellValue;

                if(numericValue % 1 != 0)
                {
                    diagnostics.Add(new SourceDiagnostic
                    {
                        Severity = DiagnosticSeverity.Error,
                        SheetName = sheetName,
                        Row = excelRowNumber,
                        Column = columnIndex + 1,
                        FieldName = fieldName,
                        Message = $"Expected integer, got {numericValue}."
                    });

                    return false;
                }

                value = (int)numericValue;
                return true;

            case CellType.String:
                string text = cell.StringCellValue.Trim();

                if(int.TryParse(text, out value))
                {
                    return true;
                }

                diagnostics.Add(new SourceDiagnostic
                {
                    Severity = DiagnosticSeverity.Error,
                    SheetName = sheetName,
                    Row = excelRowNumber,
                    Column = columnIndex + 1,
                    FieldName = fieldName,
                    Message =
                    $"Expected integer, got '{text}'."
                });

                return false;

            case CellType.Formula:
                diagnostics.Add(new SourceDiagnostic
                {
                    Severity = DiagnosticSeverity.Error,
                    SheetName = sheetName,
                    Row = excelRowNumber,
                    Column = columnIndex + 1,
                    FieldName = fieldName,
                    Message = "Formula cells are not supported yet."
                });

                return false;

            default:
                diagnostics.Add(new SourceDiagnostic
                {
                    Severity = DiagnosticSeverity.Error,
                    SheetName = sheetName,
                    Row = excelRowNumber,
                    Column = columnIndex + 1,
                    FieldName = fieldName,
                    Message = $"Unsupported cell type: {cell.CellType}"
                });

                return false;
        }
    }

    private bool TryParseRow(
        ISheet sheet,
        IRow row,
        int rowIndex,
        Dictionary<string, int> headers,
        ContentLoadResult result,
        out ContentRecord record
    )
    {
        record = null;

        int excelRowNumber = rowIndex + 1;

        bool ok = true;

        ok &= TryReadString(
            row,
            headers,
            "Id",
            sheet.SheetName,
            excelRowNumber,
            result.Diagnostics,
            out string id);

        ok &= TryReadString(
            row,
            headers,
            "Name",
            sheet.SheetName,
            excelRowNumber,
            result.Diagnostics,
            out string name);

        ok &= TryReadInt(
            row,
            headers,
            "ContentType",
            sheet.SheetName,
            excelRowNumber,
            result.Diagnostics,
            out int contentType);

        if (!ok){
            return false;
        }
        
        record = new ContentRecord
        {
            Definition = new ContentDefinition
            {
                Id = id,
                Name = name,
                ContentType = contentType,
                
                EvolutionFromId = ReadString(row, headers, "EvolutionFromId"),
                EvolutionToId = ReadString(row, headers, "EvolutionToId"),
                PrefabKey = ReadString(row, headers, "PrefabAddress"),
                IconKey = ReadString(row, headers, "IconKey")
            },

            Source = new ContentSourceHandle
            {
                SourceType = "Excel",
                SourceId = $"{sheet.SheetName}: Row {excelRowNumber}"
            }
        };

        return true;
    }
}
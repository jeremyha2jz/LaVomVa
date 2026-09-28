using System.Globalization;
using System.Text;
using ClosedXML.Excel;

namespace TicketsCombustible.Api.Services;

public static class ReportesExportador
{
    private static readonly string[] Headers =
        ["ID", "Fecha UTC", "Tipo", "Ticket", "Empleado", "Vehículo", "Departamento", "Combustible", "Estación", "Tanque", "Estado", "Galones", "Referencia", "Usuario"];

    public static byte[] Csv(ReporteResultado report)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Reporte,LaVomVa");
        builder.AppendLine($"Tipo,{CsvText(report.Tipo)}");
        builder.AppendLine($"Rango UTC,{CsvText(report.RangoUtc)}");
        builder.AppendLine($"Generado UTC,{report.GeneradoEnUtc.ToString("O", CultureInfo.InvariantCulture)}");
        builder.AppendLine($"Filtros,{CsvText(Filtros(report.Filtros))}");
        builder.AppendLine($"Registros,{report.Totales.Registros}");
        builder.AppendLine($"Galones,{report.Totales.Galones.ToString(CultureInfo.InvariantCulture)}");
        builder.AppendLine($"Despachos,{report.Totales.Despachos}");
        builder.AppendLine($"Tickets,{report.Totales.Tickets}");
        builder.AppendLine($"Solicitudes,{report.Totales.Solicitudes}");
        builder.AppendLine($"Inventario actual (gal),{report.Totales.InventarioActualGalones.ToString(CultureInfo.InvariantCulture)}");
        builder.AppendLine(string.Join(',', Headers.Select(CsvText)));
        foreach (var row in report.Items)
            builder.AppendLine(string.Join(',', Values(row).Select(CsvValue)));
        return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(builder.ToString())).ToArray();
    }

    public static byte[] Xlsx(ReporteResultado report)
    {
        using var workbook = new XLWorkbook();
        var summary = workbook.AddWorksheet("Resumen");
        summary.Cell(1, 1).Value = "Reporte LaVomVa";
        summary.Cell(1, 1).Style.Font.Bold = true;
        summary.Cell(2, 1).Value = "Tipo"; summary.Cell(2, 2).Value = report.Tipo;
        summary.Cell(3, 1).Value = "Rango UTC"; summary.Cell(3, 2).Value = report.RangoUtc;
        summary.Cell(4, 1).Value = "Generado UTC"; summary.Cell(4, 2).Value = report.GeneradoEnUtc;
        summary.Cell(5, 1).Value = "Filtros"; summary.Cell(5, 2).Value = Filtros(report.Filtros);
        summary.Cell(7, 1).Value = "Registros"; summary.Cell(7, 2).Value = report.Totales.Registros;
        summary.Cell(8, 1).Value = "Galones"; summary.Cell(8, 2).Value = report.Totales.Galones;
        summary.Cell(9, 1).Value = "Despachos"; summary.Cell(9, 2).Value = report.Totales.Despachos;
        summary.Cell(10, 1).Value = "Tickets"; summary.Cell(10, 2).Value = report.Totales.Tickets;
        summary.Cell(11, 1).Value = "Solicitudes"; summary.Cell(11, 2).Value = report.Totales.Solicitudes;
        summary.Cell(12, 1).Value = "Inventario actual (gal)"; summary.Cell(12, 2).Value = report.Totales.InventarioActualGalones;
        AddGroups(summary, 14, "Consumo por departamento", report.PorDepartamento);
        var groupStart = 14 + report.PorDepartamento.Count + 3;
        AddGroups(summary, groupStart, "Consumo por combustible", report.PorCombustible);
        summary.Columns().AdjustToContents();

        var detail = workbook.AddWorksheet("Detalle");
        detail.Cell(1, 1).InsertData(new[] { Headers });
        detail.Row(1).Style.Font.Bold = true;
        detail.Row(1).Style.Fill.BackgroundColor = XLColor.FromHtml("#f1dddd");
        var rowNumber = 2;
        foreach (var row in report.Items)
        {
            var values = Values(row);
            for (var column = 0; column < values.Length; column++)
            {
                var cell = detail.Cell(rowNumber, column + 1);
                var value = values[column];
                if (column == 0) cell.Value = row.Id;
                else if (column == 1) cell.Value = row.FechaUtc;
                else if (column == 11) cell.Value = row.Galones;
                else cell.Value = SafeSpreadsheetText(value);
            }
            rowNumber++;
        }
        detail.Row(1).Style.Font.Bold = true;
        detail.SheetView.FreezeRows(1);
        detail.RangeUsed()?.SetAutoFilter();
        detail.Columns().AdjustToContents();
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    public static byte[] Pdf(ReporteResultado report)
    {
        var lines = new List<string>
        {
            $"REPORTE LAVOMVA: {report.Tipo.ToUpperInvariant()}",
            $"Generado UTC: {report.GeneradoEnUtc:yyyy-MM-dd HH:mm:ss}",
            $"Rango: {report.RangoUtc}",
            $"Filtros: {Filtros(report.Filtros)}",
            $"Registros: {report.Totales.Registros}; galones: {report.Totales.Galones:0.00}; despachos: {report.Totales.Despachos}",
            $"Tickets: {report.Totales.Tickets}; solicitudes: {report.Totales.Solicitudes}; inventario actual: {report.Totales.InventarioActualGalones:0.00} gal",
            "Galones por departamento:"
        };
        lines.AddRange(report.PorDepartamento.Select(x => $"  {x.Nombre}: {x.Galones:0.00} gal ({x.Cantidad})"));
        lines.Add("Galones por combustible:");
        lines.AddRange(report.PorCombustible.Select(x => $"  {x.Nombre}: {x.Galones:0.00} gal ({x.Cantidad})"));
        lines.Add("DETALLE");
        lines.Add(string.Join(" | ", Headers));
        lines.AddRange(report.Items.Select(row => string.Join(" | ", Values(row).Select(x => x ?? ""))));
        if (report.Items.Count == 0) lines.Add("No hay datos para los filtros seleccionados.");
        return PdfActaCierreGenerator.GenerarDocumento(lines);
    }

    public static string Filtros(ReporteFiltros f) => string.Join("; ", new[]
    {
        f.Desde is null ? null : $"desde={f.Desde:yyyy-MM-dd}",
        f.Hasta is null ? null : $"hasta={f.Hasta:yyyy-MM-dd}",
        f.DepartamentoId is null ? null : $"departamentoId={f.DepartamentoId}",
        f.CombustibleId is null ? null : $"combustibleId={f.CombustibleId}",
        f.EmpleadoId is null ? null : $"empleadoId={f.EmpleadoId}",
        f.VehiculoId is null ? null : $"vehiculoId={f.VehiculoId}",
        f.Estado is null ? null : $"estado={f.Estado}",
        f.EstacionId is null ? null : $"estacionId={f.EstacionId}"
    }.Where(x => x is not null));

    private static string[] Values(ReporteFila x) =>
        [x.Id.ToString(CultureInfo.InvariantCulture), x.FechaUtc.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            x.Tipo, x.Ticket ?? "", x.Empleado ?? "", x.Vehiculo ?? "", x.Departamento ?? "", x.Combustible ?? "",
            x.Estacion ?? "", x.Tanque ?? "", x.Estado ?? "", x.Galones.ToString("0.00", CultureInfo.InvariantCulture),
            x.Referencia ?? "", x.Usuario ?? ""];

    private static string CsvValue(string? value)
    {
        var safe = SafeSpreadsheetText(value);
        return CsvText(safe);
    }

    private static string SafeSpreadsheetText(string? value)
    {
        if (string.IsNullOrEmpty(value)) return value ?? "";
        var first = value.TrimStart();
        return first.Length > 0 && first[0] is '=' or '+' or '-' or '@' or '\t' or '\r'
            ? "'" + value : value;
    }

    private static string CsvText(string? value)
    {
        var safe = SafeSpreadsheetText(value);
        return "\"" + (safe ?? "").Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }

    private static void AddGroups(IXLWorksheet sheet, int start, string title, IReadOnlyList<ReporteGrupo> groups)
    {
        sheet.Cell(start, 1).Value = title;
        sheet.Cell(start, 1).Style.Font.Bold = true;
        sheet.Cell(start + 1, 1).Value = "Categoría";
        sheet.Cell(start + 1, 2).Value = "Galones";
        sheet.Cell(start + 1, 3).Value = "Cantidad";
        var row = start + 2;
        foreach (var group in groups)
        {
            sheet.Cell(row, 1).Value = SafeSpreadsheetText(group.Nombre);
            sheet.Cell(row, 2).Value = group.Galones;
            sheet.Cell(row, 3).Value = group.Cantidad;
            row++;
        }
    }
}

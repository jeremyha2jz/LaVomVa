using System.Globalization;
using System.Text;
using System.Text.Json;
using TicketsCombustible.Api.Models;

namespace TicketsCombustible.Api.Services;

/// <summary>Builds a small, searchable PDF act from a persisted closed record without external runtime dependencies.</summary>
public static class PdfActaCierreGenerator
{
    public static byte[] Generar(CierreDiario cierre, string estacion, string responsable)
    {
        var lines = new List<string>
        {
            "CIERRE DIARIO DE COMBUSTIBLE",
            $"Cierre: {cierre.Id}",
            $"Fecha operacional UTC: {cierre.Fecha:yyyy-MM-dd}",
            $"Estacion: {estacion}",
            $"Responsable: {responsable}",
            $"Estado: {cierre.Estado}",
            $"Inventario inicial: {Galones(cierre.InventarioInicialGalones)} gal",
            $"Entradas del dia: {Galones(cierre.VolumenRecibidoGalones)} gal",
            $"Despachos/salidas por tickets: {Galones(cierre.VolumenDespachadoGalones)} gal",
            $"Otras salidas: {Galones(LeerDecimal("otrasSalidasGalones"))} gal",
            $"Ajustes netos (+/-): {Galones(cierre.AjustesGalones)} gal",
            $"Mermas: {Galones(cierre.MermasGalones)} gal",
            $"Inventario teorico final: {Galones(cierre.InventarioFinalGalones)} gal",
            $"Inventario fisico final: {Galones(cierre.InventarioFisicoGalones)} gal",
            $"Diferencia (fisico - teorico): {Galones(cierre.DiferenciaGalones)} gal",
            $"Cantidad de despachos: {cierre.CantidadDespachos}",
            $"Generado en UTC: {cierre.CerradoEn:yyyy-MM-dd HH:mm:ss}"
        };

        if (cierre.DetalleTanques.RootElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var tank in cierre.DetalleTanques.RootElement.EnumerateArray())
            {
                lines.Add($"Tanque {Texto(tank, "codigo")}: inicial {Galones(Decimal(tank, "inventarioInicialGalones"))}, " +
                    $"entradas {Galones(Decimal(tank, "entradasGalones"))}, despachos {Galones(Decimal(tank, "despachadoGalones"))}, " +
                    $"ajustes {Galones(Decimal(tank, "ajustesGalones"))}, merma {Galones(Decimal(tank, "mermasGalones"))}, " +
                    $"teorico {Galones(Decimal(tank, "inventarioTeoricoFinalGalones"))}, " +
                    $"fisico {Galones(Decimal(tank, "inventarioFisicoGalones"))}, diferencia {Galones(Decimal(tank, "diferenciaGalones"))} gal");
            }
        }

        var pages = lines.Select(Clean).Chunk(48).Select(pageLines => string.Join("\n", pageLines)).ToArray();
        return BuildPdf(pages);

        decimal LeerDecimal(string key)
        {
            if (cierre.DetalleTanques.RootElement.ValueKind != JsonValueKind.Array) return 0;
            return cierre.DetalleTanques.RootElement.EnumerateArray().Sum(item => Decimal(item, key));
        }
    }

    private static decimal Decimal(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.TryGetDecimal(out var result) ? result : 0;

    private static string Texto(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) ? value.GetString() ?? "-" : "-";

    private static string Galones(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);

    private static string Clean(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormD);
        var ascii = new string(normalized.Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray()).Normalize(NormalizationForm.FormC);
        return new string(ascii.Select(c => c is >= ' ' and <= '~' ? c : '?').ToArray());
    }

    private static string Escape(string value) => value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("(", "\\(", StringComparison.Ordinal).Replace(")", "\\)", StringComparison.Ordinal);

    private static byte[] BuildPdf(IReadOnlyList<string> pages)
    {
        var pageCount = Math.Max(1, pages.Count);
        var fontId = 3 + pageCount * 2;
        var objects = new List<byte[]> { Array.Empty<byte>() };
        objects.Add(Encoding.ASCII.GetBytes("<< /Type /Catalog /Pages 2 0 R >>"));
        var pageIds = Enumerable.Range(0, pageCount).Select(i => 3 + i * 2).ToArray();
        objects.Add(Encoding.ASCII.GetBytes($"<< /Type /Pages /Kids [{string.Join(" ", pageIds.Select(id => $"{id} 0 R"))}] /Count {pageCount} >>"));
        for (var i = 0; i < pageCount; i++)
        {
            var pageId = 3 + i * 2;
            var contentId = pageId + 1;
            var page = $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 {fontId} 0 R >> >> /Contents {contentId} 0 R >>";
            objects.Add(Encoding.ASCII.GetBytes(page));
            var pageText = pages.Count > i ? pages[i] : "CIERRE DIARIO DE COMBUSTIBLE";
            var content = "BT\n/F1 10 Tf\n50 752 Td\n14 TL\n" + string.Join("\nT*\n", pageText.Split('\n').Select(line => $"({Escape(line)}) Tj")) + "\nET";
            var contentBytes = Encoding.ASCII.GetBytes(content);
            var stream = Encoding.ASCII.GetBytes($"<< /Length {contentBytes.Length} >>\nstream\n").Concat(contentBytes).Concat(Encoding.ASCII.GetBytes("\nendstream")).ToArray();
            objects.Add(stream);
        }
        objects.Add(Encoding.ASCII.GetBytes("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>"));

        using var output = new MemoryStream();
        Write(output, "%PDF-1.4\n%\xE2\xE3\xCF\xD3\n");
        var offsets = new List<long> { 0 };
        for (var i = 1; i < objects.Count; i++)
        {
            offsets.Add(output.Position);
            Write(output, $"{i} 0 obj\n");
            output.Write(objects[i]);
            Write(output, "\nendobj\n");
        }
        var xrefOffset = output.Position;
        Write(output, $"xref\n0 {objects.Count}\n0000000000 65535 f \n");
        for (var i = 1; i < offsets.Count; i++) Write(output, $"{offsets[i]:D10} 00000 n \n");
        Write(output, $"trailer\n<< /Size {objects.Count} /Root 1 0 R >>\nstartxref\n{xrefOffset}\n%%EOF\n");
        return output.ToArray();
    }

    private static void Write(Stream stream, string value) => stream.Write(Encoding.ASCII.GetBytes(value));
}

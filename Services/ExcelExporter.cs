using System.IO.Compression;
using System.Text;
using System.Xml;
using DBViewer.Models;

namespace DBViewer.Services;

public static class ExcelExporter
{
    private static readonly string[] Headers = ["序号", "字段名称", "数据类型", "长度 / 精度", "可为空", "主键", "默认值", "字段注释", "外键目标"];
    private const string Ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    public static byte[] Create(DatabaseSchema schema)
    {
        if (schema.Tables.Count == 0) throw new ArgumentException("当前数据库没有可导出的表。");
        if (schema.Tables.Count > 20000 || schema.Tables.Any(t => t.Columns.Count > 10000)) throw new ArgumentException("数据库结构超过导出限制。");
        var names = SheetNames(schema.Tables);
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
        {
            Write(zip, "[Content_Types].xml", x => {
                x.WriteStartElement("Types", "http://schemas.openxmlformats.org/package/2006/content-types");
                x.WriteStartElement("Default"); x.WriteAttributeString("Extension", "rels"); x.WriteAttributeString("ContentType", "application/vnd.openxmlformats-package.relationships+xml"); x.WriteEndElement();
                x.WriteStartElement("Default"); x.WriteAttributeString("Extension", "xml"); x.WriteAttributeString("ContentType", "application/xml"); x.WriteEndElement();
                Override(x, "/xl/workbook.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml");
                for (var i = 0; i < names.Count; i++) Override(x, $"/xl/worksheets/sheet{i + 1}.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml");
                x.WriteEndElement();
            });
            Write(zip, "_rels/.rels", x => {
                x.WriteStartElement("Relationships", "http://schemas.openxmlformats.org/package/2006/relationships");
                Relationship(x, "rId1", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument", "xl/workbook.xml");
                x.WriteEndElement();
            });
            Write(zip, "xl/workbook.xml", x => {
                x.WriteStartElement("workbook", Ns);
                x.WriteAttributeString("xmlns", "r", null, "http://schemas.openxmlformats.org/officeDocument/2006/relationships");
                x.WriteStartElement("sheets", Ns);
                for (var i = 0; i < names.Count; i++) {
                    x.WriteStartElement("sheet", Ns); x.WriteAttributeString("name", names[i]); x.WriteAttributeString("sheetId", (i + 1).ToString());
                    x.WriteAttributeString("r", "id", "http://schemas.openxmlformats.org/officeDocument/2006/relationships", $"rId{i + 1}"); x.WriteEndElement();
                }
                x.WriteEndElement(); x.WriteEndElement();
            });
            Write(zip, "xl/_rels/workbook.xml.rels", x => {
                x.WriteStartElement("Relationships", "http://schemas.openxmlformats.org/package/2006/relationships");
                for (var i = 0; i < names.Count; i++) Relationship(x, $"rId{i + 1}", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet", $"worksheets/sheet{i + 1}.xml");
                x.WriteEndElement();
            });
            for (var i = 0; i < names.Count; i++) {
                var table = schema.Tables[i];
                Write(zip, $"xl/worksheets/sheet{i + 1}.xml", x => Sheet(x, table, schema.Relations));
            }
        }
        return stream.ToArray();
    }

    private static List<string> SheetNames(List<TableSchema> tables)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();
        foreach (var table in tables) {
            var name = new string(table.Name.Where(c => !"[]:*?/\\".Contains(c) && !char.IsControl(c)).ToArray()).Trim('\'', ' ');
            if (name.Length == 0) name = "表";
            if (name.Length > 31) name = name[..31];
            var candidate = name;
            for (var n = 2; !used.Add(candidate); n++) {
                var suffix = $" ({n})";
                candidate = name[..Math.Min(name.Length, 31 - suffix.Length)] + suffix;
            }
            result.Add(candidate);
        }
        return result;
    }

    private static void Sheet(XmlWriter x, TableSchema table, List<Relation> relations)
    {
        x.WriteStartElement("worksheet", Ns);
        x.WriteStartElement("sheetViews", Ns); x.WriteStartElement("sheetView", Ns); x.WriteAttributeString("workbookViewId", "0");
        x.WriteStartElement("pane", Ns); x.WriteAttributeString("ySplit", "4"); x.WriteAttributeString("topLeftCell", "A5"); x.WriteAttributeString("state", "frozen"); x.WriteEndElement();
        x.WriteEndElement(); x.WriteEndElement();
        x.WriteStartElement("cols", Ns);
        int[] widths = [8, 30, 20, 17, 12, 10, 28, 70, 45];
        for (var i = 0; i < widths.Length; i++) { x.WriteStartElement("col", Ns); x.WriteAttributeString("min", (i + 1).ToString()); x.WriteAttributeString("max", (i + 1).ToString()); x.WriteAttributeString("width", widths[i].ToString()); x.WriteAttributeString("customWidth", "1"); x.WriteEndElement(); }
        x.WriteEndElement();
        x.WriteStartElement("sheetData", Ns);
        Row(x, 1, ["表名", table.Name]);
        Row(x, 2, ["Schema", table.Schema]);
        Row(x, 3, ["表注释", table.Comment ?? ""]);
        Row(x, 4, Headers);
        for (var i = 0; i < table.Columns.Count; i++) {
            var c = table.Columns[i];
            var targets = relations.Where(r => r.FromSchema == table.Schema && r.FromTable == table.Name && r.FromColumn == c.Name)
                .Select(r => $"{r.ToSchema}.{r.ToTable}.{r.ToColumn}");
            Row(x, i + 5, [(i + 1).ToString(), c.Name, c.Type, c.Length ?? "", c.Nullable ? "是" : "否", c.PrimaryKey ? "是" : "否", c.DefaultValue ?? "", c.Comment ?? "", string.Join("; ", targets)]);
        }
        x.WriteEndElement();
        x.WriteStartElement("autoFilter", Ns); x.WriteAttributeString("ref", $"A4:I{Math.Max(4, table.Columns.Count + 4)}"); x.WriteEndElement();
        x.WriteEndElement();
    }

    private static void Row(XmlWriter x, int number, IEnumerable<string> cells)
    {
        x.WriteStartElement("row", Ns); x.WriteAttributeString("r", number.ToString());
        var index = 0;
        foreach (var value in cells) {
            index++;
            x.WriteStartElement("c", Ns); x.WriteAttributeString("r", $"{(char)('A' + index - 1)}{number}"); x.WriteAttributeString("t", "inlineStr");
            x.WriteStartElement("is", Ns); x.WriteStartElement("t", Ns); x.WriteAttributeString("xml", "space", null, "preserve");
            x.WriteString(new string((value ?? "").Where(XmlConvert.IsXmlChar).ToArray()));
            x.WriteEndElement(); x.WriteEndElement(); x.WriteEndElement();
        }
        x.WriteEndElement();
    }

    private static void Write(ZipArchive zip, string path, Action<XmlWriter> action)
    {
        using var stream = zip.CreateEntry(path, CompressionLevel.Fastest).Open();
        using var writer = XmlWriter.Create(stream, new XmlWriterSettings { Encoding = new UTF8Encoding(false), CloseOutput = false });
        writer.WriteStartDocument(); action(writer); writer.WriteEndDocument();
    }
    private static void Override(XmlWriter x, string path, string contentType) { x.WriteStartElement("Override"); x.WriteAttributeString("PartName", path); x.WriteAttributeString("ContentType", contentType); x.WriteEndElement(); }
    private static void Relationship(XmlWriter x, string id, string type, string target) { x.WriteStartElement("Relationship"); x.WriteAttributeString("Id", id); x.WriteAttributeString("Type", type); x.WriteAttributeString("Target", target); x.WriteEndElement(); }
}

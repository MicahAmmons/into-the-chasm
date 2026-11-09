using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Chasm.Core.Grid;

/// <summary>Writes a cell as "q,r". Reads "q,r", [q, r] or {"Q":..,"R":..}.</summary>
public sealed class CellCoordJsonConverter : JsonConverter<CellCoord>
{
    public override CellCoord Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.String:
                return Parse(reader.GetString()!);
            case JsonTokenType.StartArray:
                reader.Read(); int q = reader.GetInt32();
                reader.Read(); int r = reader.GetInt32();
                reader.Read();
                return new CellCoord(q, r);
        }
        int qq = 0, rr = 0;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            string name = reader.GetString()!;
            reader.Read();
            if (name.Equals("Q", StringComparison.OrdinalIgnoreCase)) qq = reader.GetInt32();
            else if (name.Equals("R", StringComparison.OrdinalIgnoreCase)) rr = reader.GetInt32();
        }
        return new CellCoord(qq, rr);
    }

    public static CellCoord Parse(string s)
    {
        var parts = s.Split(',');
        return new CellCoord(int.Parse(parts[0].Trim()), int.Parse(parts[1].Trim()));
    }

    public override void Write(Utf8JsonWriter writer, CellCoord value, JsonSerializerOptions options) =>
        writer.WriteStringValue($"{value.Q},{value.R}");
}

/// <summary>
/// Writes a list of cells on one line: "12,3 12,4 13,4". Keeps tile files short and readable even
/// with large painted regions. Reads that form or a normal JSON array.
/// </summary>
public sealed class CellListJsonConverter : JsonConverter<List<CellCoord>>
{
    public override List<CellCoord> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var list = new List<CellCoord>();
        if (reader.TokenType == JsonTokenType.String)
        {
            foreach (var part in reader.GetString()!.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                list.Add(CellCoordJsonConverter.Parse(part));
            return list;
        }
        var single = new CellCoordJsonConverter();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            list.Add(single.Read(ref reader, typeof(CellCoord), options));
        return list;
    }

    public override void Write(Utf8JsonWriter writer, List<CellCoord> value, JsonSerializerOptions options)
    {
        var sb = new StringBuilder();
        foreach (var c in value) { if (sb.Length > 0) sb.Append(' '); sb.Append(c.Q).Append(',').Append(c.R); }
        writer.WriteStringValue(sb.ToString());
    }
}

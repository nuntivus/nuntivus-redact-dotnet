using System.Globalization;
using System.Text;

namespace NuntivusRedact.Formats.Office;

internal sealed class OfficeString : IOfficeString
{
    public bool HasEscapes(string value)
    {
        for (int i = 0; i < value.Length; i++) if (Escape(value, i, out _)) return true;
        return false;
    }

    public string Decode(string value)
    {
        var result = new StringBuilder(value.Length);
        for (int i = 0; i < value.Length; i++)
        {
            if (Escape(value, i, out char decoded)) { result.Append(decoded); i += 6; }
            else result.Append(value[i]);
        }
        return result.ToString();
    }

    public string Encode(string value)
    {
        var result = new StringBuilder(value.Length);
        for (int i = 0; i < value.Length; i++)
            if (Escape(value, i, out _)) result.Append("_x005F_");
            else result.Append(value[i]);
        return result.ToString();
    }

    public void RejectEscapes(string value)
    {
        if (HasEscapes(value)) throw new RedactionException(RedactionError.UnsupportedFormat);
    }

    private bool Escape(string value, int offset, out char character)
    {
        character = default;
        if (offset > value.Length - 7 || value[offset] != '_' || value[offset + 1] != 'x' || value[offset + 6] != '_') return false;
        if (!ushort.TryParse(value.AsSpan(offset + 2, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out ushort code)) return false;
        character = (char)code;
        return true;
    }
}

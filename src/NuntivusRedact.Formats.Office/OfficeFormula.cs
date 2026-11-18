namespace NuntivusRedact.Formats.Office;

// Deliberately small allowlisted grammar; never evaluates formulas or resolves names/links.
internal sealed class OfficeFormula(string text, int maximumDepth)
{
    private int position;
    internal bool IsSupported() => Expression(0) && End();

    private bool Expression(int depth)
    {
        if (depth > maximumDepth || !Primary(depth + 1)) return false;
        while (true)
        {
            Space();
            if (position == text.Length || text[position] is not ('+' or '-' or '*' or '/' or '^')) return true;
            position++;
            if (!Primary(depth + 1)) return false;
        }
    }

    private bool Primary(int depth)
    {
        if (depth > maximumDepth) return false;
        Space();
        if (position == text.Length) return false;
        if (text[position] is '+' or '-') { position++; return Primary(depth + 1); }
        if (text[position] == '(') { position++; return Expression(depth + 1) && Take(')'); }
        if (char.IsAsciiDigit(text[position]))
        {
            int numberStart = position;
            while (position < text.Length && char.IsAsciiDigit(text[position])) position++;
            if (position < text.Length && text[position] == '.')
            {
                position++;
                int start = position;
                while (position < text.Length && char.IsAsciiDigit(text[position])) position++;
                if (start == position) return false;
            }
            return double.TryParse(text.AsSpan(numberStart, position - numberStart), System.Globalization.NumberStyles.AllowDecimalPoint,
                System.Globalization.CultureInfo.InvariantCulture, out double number) && double.IsFinite(number);
        }
        int before = position;
        while (position < text.Length && char.IsAsciiLetter(text[position])) position++;
        var name = text[before..position];
        Space();
        if (position < text.Length && text[position] == '(')
        {
            if (name is not ("SUM" or "AVERAGE" or "MIN" or "MAX" or "COUNT")) return false;
            position++;
            if (!Expression(depth + 1)) return false;
            while (Take(',')) if (!Expression(depth + 1)) return false;
            return Take(')');
        }
        position = before;
        if (!Reference()) return false;
        return !Take(':') || Reference();
    }

    private bool Reference()
    {
        Space();
        if (position < text.Length && text[position] == '$') position++;
        int column = 0;
        int letters = 0;
        while (position < text.Length && char.IsAsciiLetterUpper(text[position]))
        {
            if (++letters > 3) return false;
            column = column * 26 + text[position++] - 'A' + 1;
        }
        if (column is < 1 or > 16384) return false;
        if (position < text.Length && text[position] == '$') position++;
        int row = 0;
        int digits = 0;
        while (position < text.Length && char.IsAsciiDigit(text[position]))
        {
            if (++digits > 7 || (digits == 1 && text[position] == '0')) return false;
            row = row * 10 + text[position++] - '0';
        }
        return row is >= 1 and <= 1048576;
    }

    private bool End() { Space(); return position == text.Length; }
    private bool Take(char value)
    {
        Space();
        if (position == text.Length || text[position] != value) return false;
        position++;
        return true;
    }
    private void Space() { while (position < text.Length && text[position] == ' ') position++; }
}

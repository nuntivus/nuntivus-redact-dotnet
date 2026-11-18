namespace NuntivusRedact.Formats.Office;

internal interface IOfficeString
{
    bool HasEscapes(string value);
    string Decode(string value);
    string Encode(string value);
    void RejectEscapes(string value);
}

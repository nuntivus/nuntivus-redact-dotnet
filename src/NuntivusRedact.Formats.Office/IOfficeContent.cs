namespace NuntivusRedact.Formats.Office;

internal interface IOfficeContent
{
    ValueTask TransformAsync(OfficePart part, OfficeContext context, string path);
}

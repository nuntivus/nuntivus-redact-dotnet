using System.Xml.Linq;

namespace NuntivusRedact.Formats.Office;

internal interface IExcelContent
{
    ValueTask TransformAsync(OfficePart part, OfficeContext context, string path, HashSet<XObject> handled);
}

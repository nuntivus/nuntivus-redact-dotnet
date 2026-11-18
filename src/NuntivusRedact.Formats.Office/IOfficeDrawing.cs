using System.Xml.Linq;

namespace NuntivusRedact.Formats.Office;

internal interface IOfficeDrawing
{
    ValueTask TransformAsync(XElement root, OfficeContext context, string path, HashSet<XObject> handled, bool word);
}

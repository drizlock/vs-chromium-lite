
namespace VsChromium.Server.Projects {
    public interface IPropertyCollection {
        bool TryGetBool(string propertyName, out bool value);
        bool TryGetInt(string propertyName, out int value);
        bool TryGetString(string propertyName, out string value);
    }
}

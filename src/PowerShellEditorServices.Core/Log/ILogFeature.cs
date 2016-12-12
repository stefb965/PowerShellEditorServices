
namespace Microsoft.PowerShell.EditorServices.Logging
{
    public interface ILogFeature
    {
        void WriteInfo();

        void WriteTrace();

        void WriteError();

        void WriteWarning();
    }

    public static class LogFeatureExtensions
    {
        public static ILogFeature GetLogFeature(this IEditorFeatures editorFeatures)
        {
            return editorFeatures.GetFeature<ILogFeature>();
        }
    }
}

using System;

namespace Microsoft.PowerShell.EditorServices
{
    public interface IEditorFeatures
    {
        EditorDetails EditorDetails { get; }

        void AddFeature<TFeature>(
            TFeature featureImplementation)
                where TFeature : class;

        void AddFeature<TFeature, TWrapper>(
            TFeature featureImplementation,
            TWrapper scriptingWrapper,
            string wrapperPropertyPath)
                where TFeature : class
                where TWrapper : class;

        TFeature GetFeature<TFeature>()
            where TFeature : class;

        event EventHandler<FeatureAddedEventArgs> FeatureAdded;
    }
}

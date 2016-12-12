using System;
using System.Collections.Concurrent;

namespace Microsoft.PowerShell.EditorServices
{
    internal class FeatureManager : IEditorFeatures
    {
        #region Private Fields

        private ConcurrentDictionary<string, object> featureIndex =
            new ConcurrentDictionary<string, object>();

        #endregion

        #region Properties

        public EditorDetails EditorDetails
        {
            get;
            private set;
        }

        #endregion

        #region Constructors

        public FeatureManager()
        {
            // TODO: Needs session interface
        }

        #endregion

        #region Public Methods

        public void AddFeature<TFeature>(
            TFeature featureImplementation)
                where TFeature : class
        {
            // TODO: Null check

            this.featureIndex.AddOrUpdate(
                featureImplementation.GetType().AssemblyQualifiedName,
                featureImplementation,
                (key, currentValue) => { return currentValue; });
        }

        public void AddFeature<TFeature, TWrapper>(
            TFeature featureImplementation,
            TWrapper scriptingWrapper,
            string wrapperPropertyPath)
                where TFeature : class
                where TWrapper : class
        {
            // TODO: Add API

            this.featureIndex.AddOrUpdate(
                featureImplementation.GetType().AssemblyQualifiedName,
                featureImplementation,
                (key, currentValue) => { return currentValue; });
        }

        public TFeature GetFeature<TFeature>()
            where TFeature : class
        {
            object foundFeature = null;

            // TODO: Log?
            this.featureIndex.TryGetValue(
                typeof(TFeature).AssemblyQualifiedName,
                out foundFeature);

            return foundFeature as TFeature;
        }

        #endregion

        #region Events

        public event EventHandler<FeatureAddedEventArgs> FeatureAdded;

        protected void OnFeatureAdded(object featureObject)
        {
            if (featureObject != null)
            {
                this.FeatureAdded?.Invoke(
                    this,
                    new FeatureAddedEventArgs(featureObject.GetType()));
            }
        }

        #endregion
    }
}

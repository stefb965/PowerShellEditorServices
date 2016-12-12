using System;

namespace Microsoft.PowerShell.EditorServices
{
    public class FeatureAddedEventArgs
    {
        public Type FeatureType { get; private set; }

        internal FeatureAddedEventArgs(Type featureType)
        {
            this.FeatureType = featureType;
        }
    }

}

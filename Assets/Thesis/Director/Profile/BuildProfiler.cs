using System;
using System.Collections.Generic;

namespace Thesis.Director
{
    // Board → BuildProfile: runs each feature in order. Called once per build phase, never
    // per tick (I8).
    public sealed class BuildProfiler
    {
        private readonly IBuildFeature[] features;
        private readonly string[] names;

        public BuildProfiler(IBuildFeature[] features)
        {
            if (features == null) throw new ArgumentNullException(nameof(features));
            var seen = new HashSet<string>();
            names = new string[features.Length];
            for (int i = 0; i < features.Length; i++)
            {
                if (features[i] == null) throw new ArgumentNullException(nameof(features), "feature " + i + " is null");
                if (!seen.Add(features[i].Name)) throw new ArgumentException("two features are called '" + features[i].Name + "'", nameof(features));
                names[i] = features[i].Name;
            }
            this.features = (IBuildFeature[])features.Clone();
        }

        // The proposal's five, in its order, then collector_exposure (2026-11-24, the raid strategy).
        public static BuildProfiler Default()
        {
            return new BuildProfiler(new IBuildFeature[]
            {
                new MazeLength(), new BreachVulnerability(), new ChokepointReliance(), new TowerConcentration(), new DamageTypeMix(), new CollectorExposure(),
            });
        }

        public IReadOnlyList<string> Names => names;

        public BuildProfile Profile(ProfileInput input)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            var values = new float[features.Length];
            var hasData = new bool[features.Length];
            for (int i = 0; i < features.Length; i++)
            {
                FeatureValue v = features[i].Compute(input);
                values[i] = v.Value;
                hasData[i] = v.HasData;
            }
            return new BuildProfile(BuildProfile.SchemaVersion, (string[])names.Clone(), values, hasData);
        }
    }
}

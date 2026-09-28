using Thesis.Sim;
using UnityEngine;

// Inspector home for every simulation tunable. It wraps Thesis.Sim.SimConfig
// directly (SimConfig is [Serializable]), so there is exactly one list of fields:
// adding a field to SimConfig adds it here too, and nothing is hand-copied.
//
// A freshly created asset starts from SimConfig's initialisers, which are the
// values SampleScene was actually played with (WaveSpawner / BlockManager /
// PlayerCore serialized values). Those moved here in WP4.
[CreateAssetMenu(fileName = "SimConfig", menuName = "Thesis/Sim Config")]
public class SimConfigAsset : ScriptableObject
{
    public SimConfig config = new SimConfig();
}

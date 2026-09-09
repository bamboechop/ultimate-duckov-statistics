#if UDS_PERFORMANCE_DIAGNOSTICS
using System.Globalization;
using UltimateDuckovStatistics.UI;
using UnityEngine;

namespace UltimateDuckovStatistics.Adapters;

// Explicit F11 snapshots only. Retain integer identities, never Unity objects;
// observing resources must not keep the resources under examination alive.
internal sealed class NativeUiResourceDiagnostics
{
    private const int MaximumTrackedMeshes = 4096;
    private readonly Dictionary<int, int> observedMeshOwners = new();
    private int sequence;
    private bool trackingComplete = true;

    public void WriteSnapshot(Action<string> diagnostic)
    {
        try
        {
            var meshes = Resources.FindObjectsOfTypeAll<Mesh>().Where(value => value != null).ToArray();
            var meshIds = new HashSet<int>(meshes.Select(value => value.GetInstanceID()));
            var materialCount = Resources.FindObjectsOfTypeAll<Material>().Count(value => value != null);
            var textureCount = Resources.FindObjectsOfTypeAll<Texture>().Count(value => value != null);
            var owners = Resources.FindObjectsOfTypeAll<OwnedTotemIconShadow>().Where(value => value != null).ToArray();
            var ownerIds = new HashSet<int>(owners.Select(value => value.GetInstanceID()));
            var objects = Resources.FindObjectsOfTypeAll<GameObject>().Where(value => value != null).ToArray();
            var roots = objects.Where(value => value.name == RetainedDimmerPolicy.RootName).ToArray();
            var inputOwners = objects.Where(value => value.name == "UDS native menu input owner").ToArray();
            var released = new List<string>();
            var retainedCandidates = new List<string>();
            foreach (var observed in observedMeshOwners.ToArray())
            {
                if (!meshIds.Contains(observed.Key))
                {
                    released.Add(Pair(observed.Value, observed.Key));
                    observedMeshOwners.Remove(observed.Key);
                }
                else if (!ownerIds.Contains(observed.Value))
                    retainedCandidates.Add(Pair(observed.Value, observed.Key));
            }

            var current = new List<string>();
            var missingMeshes = 0;
            foreach (var owner in owners.OrderBy(value => value.GetInstanceID()))
            {
                var ownerId = owner.GetInstanceID();
                var meshId = owner.DiagnosticMeshInstanceId;
                current.Add(Pair(ownerId, meshId) + ":" + (owner.isActiveAndEnabled ? "active" : "inactive"));
                if (meshId == 0) { missingMeshes++; continue; }
                if (observedMeshOwners.ContainsKey(meshId) || observedMeshOwners.Count < MaximumTrackedMeshes)
                    observedMeshOwners[meshId] = ownerId;
                else trackingComplete = false;
            }

            diagnostic("M18ResourceSnapshot version=1 sequence=" + (++sequence).ToString(CultureInfo.InvariantCulture)
                + " utc=" + DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)
                + " frame=" + Time.frameCount.ToString(CultureInfo.InvariantCulture)
                + " meshes=" + meshes.Length.ToString(CultureInfo.InvariantCulture)
                + " materials=" + materialCount.ToString(CultureInfo.InvariantCulture)
                + " textures=" + textureCount.ToString(CultureInfo.InvariantCulture)
                + " totemOwnerCount=" + owners.Length.ToString(CultureInfo.InvariantCulture)
                + " udsRoots=[" + Objects(roots) + "] udsInputOwners=[" + Objects(inputOwners) + "]"
                + " totemOwners=[" + string.Join(",", current) + "]"
                + " observedMeshesNowAbsent=[" + string.Join(",", released) + "]"
                + " destroyedOwnerMeshCandidates=[" + string.Join(",", retainedCandidates) + "]"
                + " trackedMeshes=" + observedMeshOwners.Count.ToString(CultureInfo.InvariantCulture)
                + " missingOwnerMeshes=" + missingMeshes.ToString(CultureInfo.InvariantCulture)
                + " trackingComplete=" + (trackingComplete ? "true" : "false")
                + " globalCountsAreNotAttribution=true candidateIdsMayBeReused=true");
        }
        catch (Exception exception)
        {
            trackingComplete = false;
            diagnostic("M18ResourceSnapshot failed=" + exception.GetType().Name + ": " + exception.Message);
        }
    }

    private static string Pair(int owner, int mesh) => owner.ToString(CultureInfo.InvariantCulture)
        + ":" + mesh.ToString(CultureInfo.InvariantCulture);

    private static string Objects(IEnumerable<GameObject> values) => string.Join(",", values
        .OrderBy(value => value.GetInstanceID()).Select(value => value.GetInstanceID().ToString(CultureInfo.InvariantCulture)
            + ":" + (value.activeInHierarchy ? "active" : "inactive")));
}
#endif

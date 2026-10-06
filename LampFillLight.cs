using System.Collections.Generic;
using UnityEngine;
using Assets.Scripts.Objects;
using Assets.Scripts.Objects.Structures;

namespace BeefsSEGIPlus
{
    // Surrounds every active stationary lamp (Thing.AllILights - wall lights,
    // grow lights, etc; NOT wearables, see FlashlightFillLight for those) with
    // 8 weak, non-shadowed point lights in a horizontal ring. Fakes the
    // ambient bounce light that SEGI's voxel cone tracing fails to route
    // around a nearby occluder (e.g. a machine blocking GI to the opposite
    // wall even though the wall is otherwise unobstructed).
    //
    // Ring plane uses world Vector3.up as its normal. Stationeers maps are
    // flat planes, not spheres - WorldManager.HasGravityAtHeight takes only a
    // Y-scalar, never a per-position direction, and the horizon curvature is
    // a visual shader effect only - so a constant up axis is correct on
    // planet/station floors. Not verified inside a flying rocket's own local
    // frame, if that can tilt independently; low priority since fill lights
    // only matter near a lamp a player is looking at.
    //
    // Not shadowed, same reasoning as FlashlightFillLight: the thing we're
    // faking here is light *failing* to be blocked by real GI's own occluder
    // handling, so reproducing that same occlusion on the fake fill lights
    // would defeat the point.
    //
    // renderMode = ForceVertex (docs: per-light Render Mode "Not Important"
    // is always resolved as a per-vertex/SH light, never the per-pixel pass -
    // https://docs.unity3d.com/Manual/PerPixelLights-BuiltIn.html) forces
    // these onto Unity's per-vertex/SH lighting path instead of the per-pixel
    // Standard-shader BRDF pass. SH/vertex lighting has no specular term at
    // all (it's a low-frequency diffuse irradiance approximation), which is
    // exactly "don't factor into specularity". Checked the actual game
    // materials (Glass.mat etc. in the user's AssetRipper export) - window
    // glass uses the Standard shader with _SpecularHighlights/
    // _GlossyReflections on, so its visible response to a light is almost
    // entirely the per-pixel specular/reflection term; pushing our lights
    // off the per-pixel path should make them read as effectively invisible
    // on glass too, as a side effect of the same one change - reasoned from
    // how Standard-shader transparency renders, not independently confirmed
    // against this exact glass shader, so worth checking by eye once live.
    public class LampFillLightManager : MonoBehaviour
    {
        private const int RingCount = 8;

        // Half a 2x2m grid box - keeps each fill light inside the
        // neighboring cell rather than stacked on the lamp's own cell.
        // Not exposed as a slider (only Strength/Falloff were asked for);
        // ask if this needs to be tunable too.
        private const float RingRadius = 1.0f;

        // Culls lamps far from the camera so a large base with many lamps
        // doesn't keep thousands of live Lights active at once.
        private const float MaxSourceDistance = 40f;

        private static readonly Vector3[] RingDirections = BuildRingDirections();

        private static Vector3[] BuildRingDirections()
        {
            var dirs = new Vector3[RingCount];
            for (int i = 0; i < RingCount; i++)
            {
                float angle = i * (360f / RingCount) * Mathf.Deg2Rad;
                dirs[i] = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            }
            return dirs;
        }

        private class Ring
        {
            public readonly GameObject[] Objects = new GameObject[RingCount];
            public readonly Light[] Lights = new Light[RingCount];
            public bool SeenThisFrame;
        }

        private readonly Dictionary<Light, Ring> _rings = new Dictionary<Light, Ring>();

        private void Update()
        {
            bool orthoOn = SEGIPlugin.LampFillOrthogonal?.Value ?? true;
            bool diagOn = SEGIPlugin.LampFillDiagonal?.Value ?? true;

            bool active = (SEGIPlugin.Enabled?.Value ?? false)
                && (SEGIPlugin.LampFillPower?.Value ?? 0f) > 0f
                && (orthoOn || diagOn)
                && (SEGIPlugin.Instance?.IsInGameWorld() ?? false)
                && Camera.main != null;

            if (!active)
            {
                DisableAllRings();
                return;
            }

            float power = SEGIPlugin.LampFillPower.Value;
            float range = SEGIPlugin.LampFillRange?.Value ?? 3f;
            Vector3 camPos = Camera.main.transform.position;
            float maxDistSq = MaxSourceDistance * MaxSourceDistance;

            foreach (var ring in _rings.Values) ring.SeenThisFrame = false;

            var sources = Thing.AllILights;
            if (sources != null)
            {
                for (int i = 0; i < sources.Count; i++)
                {
                    ILight il = sources[i];
                    if (il == null) continue;
                    Light src = il.Light;
                    if (src == null || !src.enabled || src.intensity <= 0f) continue;

                    Vector3 srcPos = src.transform.position;
                    if ((srcPos - camPos).sqrMagnitude > maxDistSq) continue;

                    if (!_rings.TryGetValue(src, out Ring ring))
                    {
                        ring = CreateRing();
                        _rings[src] = ring;
                    }
                    ring.SeenThisFrame = true;
                    UpdateRing(ring, srcPos, power, range, orthoOn, diagOn);
                }
            }

            List<Light> toForget = null;
            foreach (var kvp in _rings)
            {
                if (kvp.Value.SeenThisFrame) continue;
                if (kvp.Key == null)
                {
                    DestroyRing(kvp.Value);
                    (toForget ??= new List<Light>()).Add(kvp.Key);
                }
                else
                {
                    SetRingEnabled(kvp.Value, false);
                }
            }
            if (toForget != null)
                foreach (var k in toForget) _rings.Remove(k);
        }

        private Ring CreateRing()
        {
            var ring = new Ring();
            for (int i = 0; i < RingCount; i++)
            {
                var go = new GameObject("SEGIPlusLampFill") { hideFlags = HideFlags.HideAndDontSave };
                var light = go.AddComponent<Light>();
                light.type = LightType.Point;
                light.shadows = LightShadows.None;
                light.renderMode = LightRenderMode.ForceVertex;
                light.color = Color.white;
                ring.Objects[i] = go;
                ring.Lights[i] = light;
            }
            return ring;
        }

        // Even ring indices (0,90,180,270deg) are the orthogonal group; odd
        // indices (45,135,225,315deg) are the diagonal group - see
        // BuildRingDirections.
        private static bool IsOrthogonalIndex(int i) => (i & 1) == 0;

        private void UpdateRing(Ring ring, Vector3 sourcePos, float power, float range, bool orthoOn, bool diagOn)
        {
            for (int i = 0; i < RingCount; i++)
            {
                Light light = ring.Lights[i];
                light.enabled = IsOrthogonalIndex(i) ? orthoOn : diagOn;
                light.intensity = power;
                light.range = range;
                ring.Objects[i].transform.position = sourcePos + RingDirections[i] * RingRadius;
            }
        }

        private void SetRingEnabled(Ring ring, bool state)
        {
            for (int i = 0; i < RingCount; i++)
            {
                if (ring.Lights[i] != null) ring.Lights[i].enabled = state;
            }
        }

        private void DisableAllRings()
        {
            foreach (var ring in _rings.Values) SetRingEnabled(ring, false);
        }

        private void DestroyRing(Ring ring)
        {
            for (int i = 0; i < RingCount; i++)
            {
                if (ring.Objects[i] != null) Destroy(ring.Objects[i]);
            }
        }

        private void OnDestroy()
        {
            foreach (var ring in _rings.Values) DestroyRing(ring);
            _rings.Clear();
        }
    }
}

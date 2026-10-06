using UnityEngine;
using Assets.Scripts.Objects;
using Assets.Scripts.Objects.Items;

namespace BeefsSEGIPlus
{
    // A plain Unity point light, not part of SEGI's voxel/cone-trace pipeline at all.
    // SEGI only voxelizes the sun and emissive materials - a moving, fast-rotating
    // source like a flashlight would smear badly under SEGI's heavy temporal blend if
    // fed into that system, so this fakes a fill light near it instead of trying to
    // make the flashlight itself cast real GI.
    public class FlashlightFillLight : MonoBehaviour
    {
        // Local player's own active wearable light is always within arm's/head's
        // length of the camera; anyone else's lit flashlight/headlamp normally isn't.
        private const float MaxSearchDistance = 2.0f;

        private Light _light;
        private GameObject _lightObject;

        private void Update()
        {
            if (!(SEGIPlugin.Enabled?.Value ?? false))
            {
                if (_light != null) _light.enabled = false;
                return;
            }

            float power = SEGIPlugin.FlashlightFillPower?.Value ?? 0f;
            if (power <= 0f)
            {
                if (_light != null) _light.enabled = false;
                return;
            }

            if (!(SEGIPlugin.Instance?.IsInGameWorld() ?? false))
            {
                if (_light != null) _light.enabled = false;
                return;
            }

            Camera cam = Camera.main;
            if (cam == null)
            {
                if (_light != null) _light.enabled = false;
                return;
            }

            Light source = FindNearestActiveWearableLight(cam.transform.position);
            if (source == null)
            {
                if (_light != null) _light.enabled = false;
                return;
            }

            EnsureLight();
            _light.enabled = true;
            _light.intensity = power;
            _light.range = SEGIPlugin.FlashlightFillRange?.Value ?? 3f;
            _lightObject.transform.position = source.transform.position;
        }

        private void EnsureLight()
        {
            if (_light != null) return;
            _lightObject = new GameObject("SEGIPlusFlashlightFill") { hideFlags = HideFlags.HideAndDontSave };
            _light = _lightObject.AddComponent<Light>();
            _light.type = LightType.Point;
            _light.shadows = LightShadows.None;
            // Forces the per-vertex/SH lighting path instead of per-pixel -
            // no specular term, and negligible visible effect on glass/window
            // shaders whose response to a light is mostly per-pixel
            // specular/reflection. See LampFillLight.cs for the full writeup.
            _light.renderMode = LightRenderMode.ForceVertex;
            _light.color = Color.white;
        }

        private static Light FindNearestActiveWearableLight(Vector3 camPos)
        {
            var wearables = Thing.AllIWearableLights;
            if (wearables == null) return null;

            Light best = null;
            float bestDistSq = MaxSearchDistance * MaxSearchDistance;
            for (int i = 0; i < wearables.Count; i++)
            {
                IWearableLight wl = wearables[i];
                if (wl == null || !wl.OnOff) continue;
                Thing t = wl.GetAsThing;
                if (t == null || t.Lights == null) continue;
                for (int j = 0; j < t.Lights.Count; j++)
                {
                    ThingLight tl = t.Lights[j];
                    if (tl == null || tl.Light == null) continue;
                    float distSq = (tl.Light.transform.position - camPos).sqrMagnitude;
                    if (distSq < bestDistSq)
                    {
                        bestDistSq = distSq;
                        best = tl.Light;
                    }
                }
            }
            return best;
        }

        private void OnDestroy()
        {
            if (_lightObject != null) Destroy(_lightObject);
        }
    }
}

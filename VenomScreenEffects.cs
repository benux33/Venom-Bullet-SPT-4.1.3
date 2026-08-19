using EFT;
using UnityEngine;

namespace Venom.Client
{
    internal static class VenomScreenEffects
    {
        private static VenomScreenEffectsHost _host;

        internal static void EnsureHost()
        {
            if (_host != null) return;
            GameObject root = new GameObject("Venom_ScreenEffects");
            UnityEngine.Object.DontDestroyOnLoad(root);
            _host = root.AddComponent<VenomScreenEffectsHost>();
        }

        internal static void SetTremor(Player player, float strength01)
        {
            if (player == null || !player.IsYourPlayer) return;
            EnsureHost();
            _host.SetTremor(Mathf.Clamp01(strength01));
        }

        internal static void ResetAll()
        {
            if (_host != null) _host.ResetState();
        }
    }

    internal sealed class VenomScreenEffectsHost : MonoBehaviour
    {
        private float _tremor;
        private Camera _shakeCamera;
        private Quaternion _appliedShake = Quaternion.identity;
        private bool _shakeApplied;

        internal void SetTremor(float strength01)
        {
            _tremor = strength01;
        }

        internal void ResetState()
        {
            RemoveAppliedShake();
            _tremor = 0f;
        }

        private void LateUpdate()
        {
            // Remove only the offset Venom applied last frame so Tarkov and
            // other camera systems keep full ownership of the base rotation.
            RemoveAppliedShake();

            if (_tremor <= 0f) return;
            Camera camera = Camera.main;
            if (camera == null) return;

            _shakeCamera = camera;
            float amount = Mathf.Lerp(0.12f, 0.72f, _tremor);
            float yaw = (Mathf.PerlinNoise(Time.unscaledTime * 13.1f, 0.3f) - 0.5f) * amount;
            float pitch = (Mathf.PerlinNoise(0.8f, Time.unscaledTime * 15.7f) - 0.5f) * amount;
            float roll = (Mathf.PerlinNoise(Time.unscaledTime * 11.9f, 2.1f) - 0.5f) * amount * 0.55f;

            _appliedShake = Quaternion.Euler(pitch, yaw, roll);
            _shakeCamera.transform.localRotation =
                _shakeCamera.transform.localRotation * _appliedShake;
            _shakeApplied = true;
        }

        private void RemoveAppliedShake()
        {
            if (!_shakeApplied || _shakeCamera == null)
            {
                _shakeApplied = false;
                return;
            }

            try
            {
                _shakeCamera.transform.localRotation =
                    _shakeCamera.transform.localRotation * Quaternion.Inverse(_appliedShake);
            }
            catch
            {
                // The raid camera can be destroyed/replaced during transitions.
            }

            _shakeApplied = false;
            _appliedShake = Quaternion.identity;
        }
    }
}

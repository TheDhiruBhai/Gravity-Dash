using UnityEngine;
using UnityEngine.InputSystem;

namespace GravityDrift
{
    public class StyleManager : MonoBehaviour
    {
        public enum ShadingStyle
        {
            CelShaded = 0,
            Painterly = 1
        }

        public ShadingStyle currentStyle = ShadingStyle.CelShaded;
        public Material[] managedMaterials;

        private void Start()
        {
            ApplyStyle(currentStyle, false);
        }

        private void Update()
        {
            if (Time.timeScale <= 0.001f) return;

            // C = Cel-Shaded, V = Painterly (P is reserved for Previous PCG Seed)
            if (InputHelper.GetKeyDown(Key.C))
            {
                ApplyStyle(ShadingStyle.CelShaded, true);
            }
            else if (InputHelper.GetKeyDown(Key.V))
            {
                ApplyStyle(ShadingStyle.Painterly, true);
            }
        }

        public void ApplyStyle(ShadingStyle style)
        {
            ApplyStyle(style, true);
        }

        public void ApplyStyle(ShadingStyle style, bool logChange)
        {
            currentStyle = style;
            float val = (style == ShadingStyle.Painterly) ? 1.0f : 0.0f;

            if (managedMaterials != null)
            {
                foreach (var mat in managedMaterials)
                {
                    if (mat != null)
                    {
                        mat.SetFloat("_RenderStyle", val);
                        if (style == ShadingStyle.Painterly)
                        {
                            mat.EnableKeyword("_RENDERSTYLE_PAINTERLY");
                            mat.DisableKeyword("_RENDERSTYLE_CELSHADED");
                        }
                        else
                        {
                            mat.EnableKeyword("_RENDERSTYLE_CELSHADED");
                            mat.DisableKeyword("_RENDERSTYLE_PAINTERLY");
                        }
                    }
                }
            }

            if (logChange)
                Debug.Log($"<color=#00e1ff>[STYLE MANAGER]</color> Switched shading style to: <b>{style}</b>");
        }
    }
}

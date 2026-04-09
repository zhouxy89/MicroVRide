using UnityEngine;
using System.Collections.Generic;

namespace HighlightPlus {


    public enum BlockerTargetOptions {
        OnlyThisObject,
        Children,
        LayerInChildren
    }

    [DefaultExecutionOrder(100)]
    [ExecuteInEditMode]
    public class HighlightEffectBlocker : MonoBehaviour {

        public BlockerTargetOptions include = BlockerTargetOptions.OnlyThisObject;
        public LayerMask layerMask = -1;
        public string nameFilter;
        public bool useRegEx;

        public bool blockOutlineAndGlow = true;
        public bool blockOverlay = true;

        Material blockerOutlineAndGlowMat;
        Material blockerOverlayMat;
        Material blockerAllMat;
        
        List<Renderer> renderers;

        void OnEnable () {
            if (blockerOutlineAndGlowMat == null) {
                blockerOutlineAndGlowMat = Resources.Load<Material>("HighlightPlus/HighlightBlockerOutlineAndGlow");
            }
            if (blockerOverlayMat == null) {
                blockerOverlayMat = Resources.Load<Material>("HighlightPlus/HighlightBlockerOverlay");
            }
            if (blockerAllMat == null) {
                blockerAllMat = Resources.Load<Material>("HighlightPlus/HighlightUIMask");
            }
            Refresh();
        }

        public void Refresh() {
            if (renderers == null) {
                renderers = new List<Renderer>();
            } else {
                renderers.Clear();
            }
            switch (include) {
                case BlockerTargetOptions.OnlyThisObject:
                    Renderer r = GetComponent<Renderer>();
                    if (r != null) renderers.Add(r);
                    return;
                case BlockerTargetOptions.Children:
                    GetComponentsInChildren<Renderer>(true, renderers);
                    break;
                case BlockerTargetOptions.LayerInChildren:
                    Renderer[] childRenderers = GetComponentsInChildren<Renderer>(true);
                    for (int k = 0; k < childRenderers.Length; k++) {
                        Renderer cr = childRenderers[k];
                        if (cr != null && ((1 << cr.gameObject.layer) & layerMask) != 0) {
                            renderers.Add(cr);
                        }
                    }
                    break;
            }
            if (!string.IsNullOrEmpty(nameFilter)) {
                for (int k = renderers.Count - 1; k >= 0; k--) {
                    string objName = renderers[k].name;
                    if (useRegEx) {
                        if (!System.Text.RegularExpressions.Regex.IsMatch(objName, nameFilter)) {
                            renderers.RemoveAt(k);
                        }
                    } else if (!objName.Contains(nameFilter)) {
                        renderers.RemoveAt(k);
                    }
                }
            }
        }

        void Update () {
            int stencilID = 0;
            if (blockOutlineAndGlow) {
                stencilID = 2;
            }
            if (blockOverlay) {
                stencilID += 4;
            }
            if (stencilID == 0 || renderers == null) return;

            Material mat = null;
            if (stencilID == 2) {
                mat = blockerOutlineAndGlowMat;
            }
            else if (stencilID == 4) {
                mat = blockerOverlayMat;
            }
            else if (stencilID == 6) {
                mat = blockerAllMat;
            }

            if (mat == null) return;

            int renderersCount = renderers.Count;
            for (int k = 0; k < renderersCount; k++) {
                Renderer r = renderers[k];
                if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                
                // Handle different renderer types
                if (r is MeshRenderer) {
                    MeshFilter mf = r.GetComponent<MeshFilter>();
                    if (mf != null && mf.sharedMesh != null) {
                        int submeshCount = r.sharedMaterials.Length;
                        for (int i = 0; i < submeshCount; i++) {
                            Graphics.DrawMesh(mf.sharedMesh, r.transform.localToWorldMatrix, mat, r.gameObject.layer, null, i);
                        }
                    }
                }
                else if (r is SkinnedMeshRenderer) {
                    SkinnedMeshRenderer smr = r as SkinnedMeshRenderer;
                    if (smr.sharedMesh != null) {
                        int submeshCount = r.sharedMaterials.Length;
                        for (int i = 0; i < submeshCount; i++) {
                            Graphics.DrawMesh(smr.sharedMesh, r.transform.localToWorldMatrix, mat, r.gameObject.layer, null, i);
                        }
                    }
                }
            }
        }
    }
}

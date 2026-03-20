using UnityEngine;using UnityEngine;

namespace PenGame.View
{
    // [ExecuteAlways] 让脚本在编辑模式下也能执行 Start/Update
    [ExecuteAlways]
    public class SpriteStackRenderer : MonoBehaviour
    {
        [Header("Settings")]
        public Sprite[] layers;
        [Range(0, 0.5f)] // 增加一个滑动条方便调试
        public float layerOffset = 0.02f;
        public string sortingLayerName = "Default";
        public int baseSortingOrder = 0;

        // 当你在 Inspector (检查器) 里改动任何数值时，这个函数会自动执行
        private void OnValidate()
        {
            // 只有当物体在场景中，且 layers 不为空时才刷新
            if (gameObject.activeInHierarchy)
            {
                InitializeStack();
            }
        }

        public void InitializeStack()
        {
            // 1. 清理旧的层级 (在编辑模式下必须使用 DestroyImmediate)
            // 我们通过循环删除，直到没有子物体为止
            while (transform.childCount > 0)
            {
                DestroyImmediate(transform.GetChild(0).gameObject);
            }

            if (layers == null || layers.Length == 0) return;

            // 2. 重新生成
            for (int i = 0; i < layers.Length; i++)
            {
                if (layers[i] == null) continue;

                GameObject layerObj = new GameObject($"Layer_{i}");
                layerObj.transform.SetParent(this.transform);
                
                // 关键：为了不让层级在 Hierarchy 里乱跳，可以给它们加上特殊标识
                // 或者干脆让它们在 Hierarchy 里不可见 (可选): 
                // layerObj.hideFlags = HideFlags.DontSave; 

                layerObj.transform.localPosition = new Vector3(0, i * layerOffset, 0);
                layerObj.transform.localRotation = Quaternion.identity;
                layerObj.transform.localScale = Vector3.one;

                SpriteRenderer sr = layerObj.AddComponent<SpriteRenderer>();
                sr.sprite = layers[i];
                sr.sortingLayerName = sortingLayerName;
                sr.sortingOrder = baseSortingOrder + i;
            }
        }
    }
}







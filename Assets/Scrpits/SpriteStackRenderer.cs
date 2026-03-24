using UnityEngine;

[ExecuteAlways]
public class SpriteStacker : MonoBehaviour
{
    [Header("素材设置")]
    public Sprite[] layers; 
    
    [Header("实时调整参数")]
    [Range(0.001f, 0.5f)] 
    public float layerSpacing = 0.02f;

    private bool isUpdating = false;

    private void OnValidate()
    {
        
        if (!isUpdating)
        {
            // 使用 UnityEditor 的延时调用，确保 Unity 已经处理完拖拽操作
            #if UNITY_EDITOR
            UnityEditor.EditorApplication.delayCall += SafeUpdate;
            #endif
        }
    }

    private void SafeUpdate()
    {
        if (this == null) return; // 防止物体被删除后依然执行
        
        #if UNITY_EDITOR
        UnityEditor.EditorApplication.delayCall -= SafeUpdate;
        #endif

        isUpdating = true;
        UpdateStack();
        isUpdating = false;
    }

    [ContextMenu("手动刷新层级")]
    public void UpdateStack()
    {
        if (layers == null || layers.Length == 0) return;


        int layerCount = layers.Length;

        // 确保子物体数量
        while (transform.childCount < layerCount)
        {
            GameObject newLayer = new GameObject($"Layer_{transform.childCount}");
            newLayer.transform.SetParent(this.transform);
            newLayer.AddComponent<SpriteRenderer>();
        }

        // 更新每一层
        for (int i = 0; i < transform.childCount; i++)
        {
            Transform child = transform.GetChild(i);
            
            if (i < layerCount)
            {
                child.gameObject.SetActive(true);
                child.localPosition = new Vector3(0, i * layerSpacing, 0);
                // 修正：90度躺平，0度代表笔的指向
                child.localRotation = Quaternion.Euler(90f, 0, 0);

                SpriteRenderer sr = child.GetComponent<SpriteRenderer>();
                sr.sprite = layers[i];
                sr.sortingOrder = i;
            }
            else
            {
                // 多出来的物体先隐藏，不要在循环里直接 Destroy
                child.gameObject.SetActive(false);
            }
        }
    }

    // 当你在 Inspector 右键脚本组件时，可以彻底清理
    [ContextMenu("彻底清理残留")]
    private void ClearAll()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            DestroyImmediate(transform.GetChild(i).gameObject);
        }
    }
}
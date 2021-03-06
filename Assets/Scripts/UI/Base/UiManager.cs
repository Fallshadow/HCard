﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.U2D;

namespace act.ui
{
    public class UiManager : SingletonMonoBehavior<UiManager>
    {
        public const int VisibleUiLayer = 5;
        public const int InvisibleUiLayer = 6;

        public static Vector2 DangerAreaSize { get; private set; }
        public Camera UiCamera { get { return uiCamera; } }
        public RectTransform MainRoot { get { return manageStrat.MainRoot; } }

        public Sprite DefaultSprite { get; private set; }
        //public DebugUi DebugUi { get; private set; }

        [Header("References")]
        [SerializeField] private Camera uiCamera = null;
        [SerializeField] private RectTransform[] canvasRoots;

        private UiManageStrategy manageStrat = null;
        private Dictionary<Type, UiBase> loadedUiDict = new Dictionary<Type, UiBase>();

        private Dictionary<string, Material> materialDict = new Dictionary<string, Material>();
        private Dictionary<string, SpriteAtlas> atlasDict = new Dictionary<string, SpriteAtlas>(11);

        protected override void Init()
        {
            manageStrat = new UiManageStrategy(canvasRoots);
        }

        private void Start()
        {
            //calculateAdaptation(); // NOTE: Waiting for CanvasScaler to initialize.
            //CreateUi<LoadingCanvas>();
        }

        public T CreateUi<T>() where T : UiBase
        {
            Type uiType = typeof(T);
            if (loadedUiDict.TryGetValue(uiType, out UiBase ui))
            {
                return ui as T;
            }

            Type attrType = typeof(BindingResourceAttribute);
            BindingResourceAttribute attr = Attribute.GetCustomAttribute(uiType, attrType) as BindingResourceAttribute;
            ui = utility.LoadResources.LoadAsset<UiBase>(attr.AssetId);
            if (ui == null)
            {
                debug.PrintSystem.LogError($"[UiManager] Load resource failed. AssetId: {attr.AssetId}");
                return null;
            }

            ui = manageStrat.CreateUi(ui);
            ui.OnCreate();
            loadedUiDict[uiType] = ui;
            return ui as T;
        }

        public void DestroyAllUi()
        {
            manageStrat.Clear();
            List<Type> destroyUis = new List<Type>(loadedUiDict.Count);
            foreach (KeyValuePair<Type, UiBase> kvPair in loadedUiDict)
            {
                if (kvPair.Value == null)
                {
                    continue;
                }

                if (kvPair.Value.IsDontDestroy)
                {
                    continue;
                }

                destroyUis.Add(kvPair.Key);
                kvPair.Value.OnRuin();
                Destroy(kvPair.Value.gameObject);
            }

            for (int i = 0, count = destroyUis.Count; i < count; ++i)
            {
                loadedUiDict.Remove(destroyUis[i]);
            }
        }

        public void OpenUi<T>(Action completeCb = null) where T : UiBase
        {
            loadedUiDict.TryGetValue(typeof(T), out UiBase ui);
            if (ui == null)
            {
                ui = CreateUi<T>();
            }

            manageStrat.OpenUi(ui, completeCb);
        }

        public void CloseUi<T>(Action completeCb = null) where T : UiBase
        {
            loadedUiDict.TryGetValue(typeof(T), out UiBase loadedUi);
            if (loadedUi == null)
            {
                debug.PrintSystem.LogWarning($"[UiManager] UI is unload. Type: {typeof(T).Name}");
                return;
            }

            loadedUi.Close(completeCb);
        }

        public void OperateUi(int op)
        {
            manageStrat.OperateUi(op);
        }

        public void RefreshUi()
        {
            foreach (KeyValuePair<Type, UiBase> kvPair in loadedUiDict)
            {
                if (kvPair.Value.State == UiState.US_SHOW)
                {
                    kvPair.Value.Refresh();
                }
            }
        }

        #region Atlas Related
        public SpriteAtlas GetAtlas(string atlasName)
        {
            atlasDict.TryGetValue(atlasName, out SpriteAtlas spriteAtlas);
            if (spriteAtlas == null)
            {
                spriteAtlas = utility.LoadResources.LoadAsset<SpriteAtlas>($"{data.ResourcesPathSetting.UiAtlasFolder}{atlasName}");
            }

            if (spriteAtlas == null)
            {
                debug.PrintSystem.LogWarning($"[UiManager] Can't load atlas: {atlasName}");
                return null;
            }

            atlasDict[atlasName] = spriteAtlas;
            return spriteAtlas;
        }

        public void ReleaseAtlas(string atlasPath)
        {
            atlasDict.TryGetValue(atlasPath, out SpriteAtlas atlas);
            if (atlas != null)
            {
                Destroy(atlas);
            }

            atlasDict.Remove(atlasPath);
        }

        public void ReleaseAllAtlas()
        {
            foreach (KeyValuePair<string, SpriteAtlas> keyValuePair in atlasDict)
            {
                Destroy(keyValuePair.Value);
            }

            atlasDict.Clear();
        }

        public Sprite GetSprite(string spriteName, string atlasName)
        {
            SpriteAtlas spriteAtlas = GetAtlas(atlasName);
            if (spriteAtlas == null)
            {
                return getDefaultSprite();
            }

            Sprite sprite = spriteAtlas.GetSprite(spriteName);
            if (sprite == null)
            {
                return getDefaultSprite();
            }

            return sprite;
        }

        private Sprite getDefaultSprite()
        {
            if (DefaultSprite == null)
            {
                DefaultSprite = utility.LoadResources.LoadAsset<Sprite>($"{data.ResourcesPathSetting.Textures}{data.ResourcesPathSetting.DefaultSpriteUiTexture}");
            }

            return DefaultSprite;
        }
        #endregion

        [Conditional("UNITY_EDITOR"), Conditional("UNITY_IOS")]
        private void calculateAdaptation()
        {
            const float MinFullScreenRatio = 17f / 9f;
            // NOTE: 以iPhoneX @1x的解析度為基準來計算.
            const float SafeAreaRatioWidth = 1f - (44f * 2f / 812f);
            const float SafeAreaRatioHeight = 1f - (21f / 375f); // NOTE: 只有縮減下邊

            if ((float)Screen.width / Screen.height < MinFullScreenRatio)
            {
                DangerAreaSize = Vector2.zero;
                return;
            }

            Vector2 SafeAreaRatio = new Vector2(SafeAreaRatioWidth, SafeAreaRatioHeight);
            Vector2 canvasSizeDelta = manageStrat.MainRoot.sizeDelta;
            Vector2 scaleRatio = new Vector2(Screen.width / canvasSizeDelta.x, Screen.height / canvasSizeDelta.y);
            DangerAreaSize = canvasSizeDelta * (Vector2.one - SafeAreaRatio) / scaleRatio;
        }

        public Vector3 WorldToUiPoint(Camera objCamera, Vector3 worldPostion)
        {
            // 將照射3D物件的Camera指定進去，並將要轉換的3D座標輸入進去
            // 就可以得到3D Camera轉換為該Space下的Screen Space.
            Vector2 screenPos = RectTransformUtility.WorldToScreenPoint(objCamera, worldPostion);

            // 最後用這個API將Screen Space轉換為UGUI的座標，分別將UI Canvas的RectTransform傳入
            // 後面依序是要轉換的ScreenPostion跟UI Camera以及輸到到那一個變數.　
            RectTransformUtility.ScreenPointToLocalPointInRectangle(manageStrat.MainRoot, screenPos, uiCamera, out Vector2 uiCameraPostion);
            return uiCameraPostion;
        }

        // public void SetDebugUi()
        // {
        //     if (data.DebugConfig.instance.ShowDebugInfo)
        //     {
        //         DebugUi = CreateUi<DebugUi>();
        //         DebugUi.Show();
        //     }
        // }
    }
}
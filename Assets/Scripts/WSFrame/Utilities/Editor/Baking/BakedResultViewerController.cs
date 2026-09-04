#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

namespace WS_Modules.Baking.Editor
{
    /// <summary>协调烘焙结果窗口用户意图、数据源读取与 Editor 事务。</summary>
    internal sealed class BakedResultViewerController : IDisposable
    {
        #region 字段

        private readonly BakedResultViewerView view;
        private readonly BakedResultEditorService service;
        private IBakedResultDataSource source;
        private bool disposed;

        #endregion

        #region 生命周期

        /// <summary>创建结果窗口 Controller 并连接 View 事件。</summary>
        /// <param name="view">结果窗口 View。</param>
        /// <param name="service">烘焙事务服务。</param>
        public BakedResultViewerController(BakedResultViewerView view, BakedResultEditorService service)
        {
            this.view = view ?? throw new ArgumentNullException(nameof(view));
            this.service = service ?? throw new ArgumentNullException(nameof(service));
            view.BakeRequested += OnBakeRequested;
            view.RefreshRequested += OnRefreshRequested;
            view.PingRequested += OnPingRequested;
        }

        /// <summary>解除 View 事件连接并释放 Controller。</summary>
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            view.BakeRequested -= OnBakeRequested;
            view.RefreshRequested -= OnRefreshRequested;
            view.PingRequested -= OnPingRequested;
            source = null;
        }

        #endregion

        #region 状态刷新

        /// <summary>绑定一个新的结果数据源并读取当前快照。</summary>
        /// <param name="dataSource">结果数据源。</param>
        public void Bind(IBakedResultDataSource dataSource)
        {
            if (disposed) return;
            source = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
            view.SetSource(source);
            Refresh();
        }

        /// <summary>重新读取数据源的最后一次烘焙结果。</summary>
        public void Refresh()
        {
            if (disposed || source == null) return;
            try { view.Render(source.CreateBakedResultTableData()); }
            catch (Exception exception) { view.ShowError(exception.Message); }
        }

        #endregion

        #region 用户意图

        /// <summary>执行数据源 Bake 事务并刷新结果表。</summary>
        private void OnBakeRequested()
        {
            if (source == null) return;
            try
            {
                service.Bake(source);
                Refresh();
            }
            catch (Exception exception)
            {
                view.ShowError(exception.Message);
            }
        }

        /// <summary>响应用户的手动刷新请求。</summary>
        private void OnRefreshRequested() => Refresh();

        /// <summary>在 Project 窗口定位当前 Unity 数据源。</summary>
        private void OnPingRequested()
        {
            if (source is not UnityEngine.Object target) return;
            Selection.activeObject = target;
            EditorGUIUtility.PingObject(target);
        }

        #endregion
    }
}
#endif

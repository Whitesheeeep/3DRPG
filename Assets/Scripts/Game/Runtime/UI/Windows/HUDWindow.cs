// WSFrame WindowCode 生成规则：
// 1. 本文件首次由生成器创建，创建后作为手写窗口逻辑入口。
// 2. 后续重新生成不会整体覆盖本文件。
// 3. 生命周期方法、API 方法、MVVM 绑定和业务逻辑不会被生成器修改。
// 4. UI 事件方法一旦存在，生成器不会覆盖其方法体。
// 5. 当 UI 新增可绑定事件组件时，生成器只会追加缺失的事件空方法。
// 6. 当 UI 删除、重命名或修改组件类型时，旧事件方法不会自动删除，请手动清理。
using DG.Tweening;
using RPG.Game.UI.Controllers;
using Sirenix.OdinInspector;

namespace WS_Modules.UIModule
{
	/// <summary>
	/// HUD 主界面窗口，负责 HUD 自身的生命周期回调、按钮意图转发和隐藏视觉效果。
	/// </summary>
	[InfoBox("依赖 HUD 根节点上的 HUDWindowDataComponent 与 HUDWindowController；Prefab 必须绑定其窗口数据和事件组件。")]
	public partial class HUDWindow : WindowBase
	{
		#region 依赖字段

		private HUDWindowController controller;

		#endregion

		#region 生命周期

		/// <summary>
		/// 初始化 HUD 的自动绑定组件，并完成窗口基类初始化。
		/// </summary>
		public override void OnAwake()
		{
			BindGeneratedComponents();
			base.OnAwake();
			controller = GameObject.GetComponent<HUDWindowController>();
			if (controller == null)
				throw new System.InvalidOperationException("[HUDWindow] 根节点缺少 HUDWindowController。");
			controller.Initialize();
		}

		/// <summary>
		/// HUD 显示时执行基类显示回调。
		/// </summary>
		public override void OnShow()
		{
			base.OnShow();
			controller?.HandleWindowShown();
		}

		/// <summary>
		/// HUD 隐藏完成时执行基类隐藏回调。
		/// </summary>
		public override void OnHide()
		{
			base.OnHide();
		}

		/// <summary>
		/// HUD 销毁时执行基类销毁回调。
		/// </summary>
		public override void OnDestroy()
		{
			controller?.Dispose();
			base.OnDestroy();
		}

		#endregion

		#region 窗口动画

		/// <summary>
		/// 创建 HUD 的隐藏渐隐动画。
		/// </summary>
		/// <returns>从当前透明度渐变到完全透明的 DOTween 动画。</returns>
		protected override Tween HideAnimation()
		{
			// HUD 的排序层级低于默认动画阈值，仅渐隐根 CanvasGroup，不改变内容缩放。
			return DOTween.To(
				() => WindowCanvasGroup.alpha,
				alpha => WindowCanvasGroup.alpha = alpha,
				0f,
				0.2f);
		}

		#endregion

		#region API

		#endregion

		#region UI 组件事件

		/// <summary>将 HUD 上的 Bag 按钮点击转换为统一的背包打开意图。</summary>
		public void OnBagButtonClick()
		{
			// 生成代码仍绑定 HUDWindow 方法；业务意图由同根 Controller 发布，保持 Window 与 MVC 边界清晰。
			controller.HandleBagButtonClicked();
		}

		#endregion
	}
}

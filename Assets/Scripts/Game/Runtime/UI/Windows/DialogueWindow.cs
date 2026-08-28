// WSFrame WindowCode 生成规则：
// 1. 本文件首次由生成器创建，创建后作为手写窗口逻辑入口。
// 2. 后续重新生成不会整体覆盖本文件。
// 3. 生命周期方法、API 方法、MVVM 绑定和业务逻辑不会被生成器修改。
// 4. UI 事件方法一旦存在，生成器不会覆盖其方法体。
// 5. 当 UI 新增可绑定事件组件时，生成器只会追加缺失的事件空方法。
// 6. 当 UI 删除、重命名或修改组件类型时，旧事件方法不会自动删除，请手动清理。
using Cysharp.Threading.Tasks;
using RPG.DialogueSystemModule;
using RPG.Game;
using RPG.Game.UI;
using UnityEngine;

namespace WS_Modules.UIModule
{
	/// <summary>
	/// 对话窗口组合根，持有对白 View、Choice View 和窗口级 Controller。
	/// </summary>
	public partial class DialogueWindow:WindowBase
	{
		#region 组合状态

		// Window 作为组合根持有 View 和 Controller；Controller 生命周期跟随窗口实例而非显隐状态。
		private DialogueSpeechView speechView;
		private DialogueChoiceView choiceView;
		private DialogueUIController controller;
		private UniTask choiceInitializationTask;

		#endregion

		 #region 生命周期函数
		 /// <summary>绑定生成字段并组装 DialogueWindow 内的 View 与 Controller。</summary>
		 public override void OnAwake()
		 {
			 BindGeneratedComponents();
			 base.OnAwake();

				speechView = new DialogueSpeechView(
					 dataCompt.AdvanceButton,
					 dataCompt.SpeakerNameTMP_Text,
					 dataCompt.SpeakContentTMP_Text,
					 dataCompt.SpeakContentTypeWriter);
			 choiceView = new DialogueChoiceView(
				 dataCompt.DialogueChoiceRootTransform,
				 dataCompt.OptionPrefabPath,
				 dataCompt.InitialChoiceRowCount);
			 DialogueSystem dialogueSystem = GameArchitecture.Interface.GetSystem<DialogueSystem>();
			 controller = new DialogueUIController(this, dialogueSystem, speechView, choiceView);
			 choiceInitializationTask = choiceView.InitializeAsync().Preserve();
		 }
		 /// <summary>窗口显示生命周期；Controller 已在 OnAwake 建立事件连接，不重复订阅。</summary>
		 public override void OnShow()
		 {
			 base.OnShow();
		 }
		 /// <summary>窗口隐藏生命周期；保留 Controller 监听以接收后续 Started 事实。</summary>
		 public override void OnHide()
		 {
			 base.OnHide();
		 }
		 /// <summary>按 Controller、View、WindowBase 顺序释放对话 UI 资源和事件连接。</summary>
		 public override void OnDestroy()
		 {
			 controller?.Dispose();
			 controller = null;
			 choiceView?.Dispose();
			 choiceView = null;
			 speechView?.Dispose();
			 speechView = null;
			 base.OnDestroy();
		 }
		 #endregion
		 #region API Function

		 /// <summary>等待 DialogueWindow 内部 Choice View 完成行资源初始化。</summary>
		 /// <returns>Choice View 初始化任务。</returns>
		 public UniTask WaitUntilReadyAsync() => choiceInitializationTask;

		 #endregion
		 #region UI组件事件
		 #endregion
	}
}

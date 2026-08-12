/*---------------------------------
 *Title:UI自动化组件生成代码生成工具
 *Date:2026/8/12 19:19:21
 *Description:变量需要以[Text]括号加组件类型的格式进行声明，然后右键窗口物体—— 一键生成UI数据组件脚本即可
 *注意:以下文件是自动生成的，再次生成后会以代码追加的形式新增,若手动修改后,尽量避免自动生成
---------------------------------*/
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace WS_Modules.UIModule
{
	public class SkillSlotRoot:MonoBehaviour
	{
		#region 自定义字段
		public  Button Skill_1Button;

		public  Image Skill_1_CDImage;

		public  TMP_Text Skill1CDTMP_Text;

		public  Button Skill_2Button;

		public  Image Skill_2_CDImage;

		public  TMP_Text Skill2CDTMP_Text;

		public  Button Skill_3Button;

		public  Image Skill_3_CDImage;

		public  TMP_Text Skill3CDTMP_Text;

		public  Button Skill_4Button;

		public  Image Skill_4_CDImage;

		public  TMP_Text Skill4CDTMP_Text;

		#endregion


		#region 生命周期
		//脚本初始化接口 (为保证生命周期的执行顺序，请在View层调用该接口确保需要初始化的数据正常执行)
		public void OnInitialize()
		{
			//按钮事件自动注册绑定
			Skill_1Button.onClick.AddListener(OnSkill_1ButtonClick);
			Skill_2Button.onClick.AddListener(OnSkill_2ButtonClick);
			Skill_3Button.onClick.AddListener(OnSkill_3ButtonClick);
			Skill_4Button.onClick.AddListener(OnSkill_4ButtonClick);
		}
		//物体设置数据接口 (请自定以你的参数，方便外部调用传参)
		public  void SetItemData()
		{
		}
		//物体销毁时执行 (为保证生命周期的执行顺序，请在View层调用该接口确保需要释放时的接口正常调用)
		public  void OnDispose()
		{
		}
		#endregion


		#region UI组件事件
		private void OnSkill_1ButtonClick()
		{
		
		}

		private void OnSkill_2ButtonClick()
		{
		
		}

		private void OnSkill_3ButtonClick()
		{
		
		}

		private void OnSkill_4ButtonClick()
		{

		}
		#endregion


	}
}

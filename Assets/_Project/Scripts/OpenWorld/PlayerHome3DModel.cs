using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using AdversityRoad.Combat;
using AdversityRoad.Goals;

namespace AdversityRoad.OpenWorld
{
    /// <summary>开放城区玩家住宅：程序化3D生活基地。只在城区已建立后安装。</summary>
    public static class PlayerHome3DModel
    {
        static readonly Dictionary<string, Material> mats = new Dictionary<string, Material>();
        static Material Mat(string key, Color color)
        {
            if (mats.TryGetValue(key, out var m)) return m;
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            m = new Material(shader); m.color = color; mats[key] = m; return m;
        }

        public static void Build(Vector3 h)
        {
            if (GameObject.Find("PlayerHome_3D_Complete") != null) return;
            var root = new GameObject("PlayerHome_3D_Complete"); root.transform.position = h;
            Color wall=new Color(.78f,.76f,.71f), floor=new Color(.42f,.29f,.19f), wood=new Color(.30f,.20f,.13f);
            Color wood2=new Color(.48f,.32f,.20f), fabric=new Color(.35f,.43f,.56f), white=new Color(.93f,.93f,.90f);
            Color metal=new Color(.55f,.58f,.62f), glass=new Color(.32f,.68f,.82f), green=new Color(.25f,.48f,.30f), pool=new Color(.10f,.55f,.78f);
            const float W=30f,D=22f,H=3.4f;

            Box(root,"Floor",h+new Vector3(0,-.04f,0),new Vector3(W,.22f,D),floor);
            Box(root,"Wall_N",h+new Vector3(0,H/2,D/2),new Vector3(W,H,.35f),wall);
            Box(root,"Wall_W",h+new Vector3(-W/2,H/2,0),new Vector3(.35f,H,D),wall);
            Box(root,"Wall_E",h+new Vector3(W/2,H/2,0),new Vector3(.35f,H,D),wall);
            Box(root,"Wall_S_L",h+new Vector3(-10.5f,H/2,-D/2),new Vector3(9,H,.35f),wall);
            Box(root,"Wall_S_R",h+new Vector3(10.5f,H/2,-D/2),new Vector3(9,H,.35f),wall);
            Box(root,"Wall_S_Top",h+new Vector3(0,3.05f,-D/2),new Vector3(6,.7f,.35f),wall);

            // 房间分区
            Box(root,"Partition_Bedroom",h+new Vector3(-6,H/2,4.8f),new Vector3(.25f,H,12.4f),wall);
            Box(root,"Partition_Study",h+new Vector3(3.8f,H/2,7.5f),new Vector3(.25f,H,7f),wall);
            Box(root,"Partition_Office",h+new Vector3(9,H/2,1.5f),new Vector3(.25f,H,8f),wall);
            Box(root,"Partition_Bath",h+new Vector3(-10.5f,H/2,-5.7f),new Vector3(9,H,.25f),wall);
            Box(root,"Partition_Kitchen",h+new Vector3(9,H/2,-7.3f),new Vector3(.25f,H,4.8f),wall);

            // 客厅 + 六人餐厅
            Box(root,"Sofa",h+new Vector3(-.5f,.55f,-2),new Vector3(5.8f,1.1f,1.8f),new Color(.38f,.40f,.46f));
            Box(root,"Coffee_Table",h+new Vector3(-.5f,.32f,.6f),new Vector3(3.4f,.6f,1.4f),wood2);
            Box(root,"TV",h+new Vector3(-.5f,1.55f,9),new Vector3(5.2f,2.7f,.16f),new Color(.08f,.10f,.13f));
            Box(root,"Dining_Table",h+new Vector3(5,.55f,-1.5f),new Vector3(5.8f,1.1f,2.4f),wood2);
            Vector3[] seats={new Vector3(2,.35f,-1.5f),new Vector3(8,.35f,-1.5f),new Vector3(2.8f,.35f,-3.2f),new Vector3(7.2f,.35f,-3.2f),new Vector3(2.8f,.35f,.2f),new Vector3(7.2f,.35f,.2f)};
            for(int i=0;i<6;i++) Box(root,"Dining_Chair_"+(i+1),h+seats[i],new Vector3(1,.7f,1),fabric);
            for(int i=0;i<6;i++) Box(root,"Place_Setting_"+(i+1),h+new Vector3(2.9f+(i%2)*4.1f,1.14f,-2.55f+(i/2)*1f),new Vector3(.38f,.04f,.38f),white);

            // 卧室：床垫、床单、被子、双枕头、床头柜、艺术画
            Vector3 bed=h+new Vector3(-10.8f,0,5.4f);
            Box(root,"Bed_Frame",bed+new Vector3(0,.35f,.4f),new Vector3(5,.7f,6.6f),wood);
            Box(root,"Bed_Mattress",bed+new Vector3(0,.82f,.4f),new Vector3(4.7f,.28f,6.25f),white);
            Box(root,"Bed_Sheet",bed+new Vector3(0,1,-.1f),new Vector3(4.55f,.08f,4.7f),new Color(.78f,.84f,.92f));
            Box(root,"Bed_Blanket",bed+new Vector3(0,1.1f,-1.25f),new Vector3(4.55f,.14f,3.15f),fabric);
            Box(root,"Pillow_Left",bed+new Vector3(-1.2f,1.15f,3.05f),new Vector3(2,.26f,1),white);
            Box(root,"Pillow_Right",bed+new Vector3(1.2f,1.15f,3.05f),new Vector3(2,.26f,1),white);
            Box(root,"Nightstand_Left",bed+new Vector3(-3.8f,.55f,2.3f),new Vector3(1.2f,1.1f,1.2f),wood2);
            Box(root,"Nightstand_Right",bed+new Vector3(3.8f,.55f,2.3f),new Vector3(1.2f,1.1f,1.2f),wood2);
            Art(root,bed+new Vector3(-4.2f,2.2f,5.2f),new Vector3(2.2f,1.5f,.1f),new Color(.22f,.42f,.54f),wood);
            Art(root,bed+new Vector3(0,2.25f,5.2f),new Vector3(2.4f,1.7f,.1f),new Color(.68f,.38f,.28f),wood);
            Art(root,bed+new Vector3(4.2f,2.2f,5.2f),new Vector3(2.2f,1.5f,.1f),new Color(.35f,.55f,.34f),wood);

            // 书房：书桌、椅子、两个大书柜、书籍
            Vector3 study=h+new Vector3(-1.6f,0,8.1f);
            Box(root,"Study_Desk",study+new Vector3(0,.55f,-1.4f),new Vector3(4.8f,1.1f,1.6f),wood2);
            Box(root,"Study_Chair",study+new Vector3(0,.4f,-3),new Vector3(1.2f,.8f,1.2f),fabric);
            for(int side=-1;side<=1;side+=2){
                Box(root,"Bookcase_"+side,study+new Vector3(side*2.8f,1.5f,2),new Vector3(.7f,3,4.6f),wood);
                for(int row=0;row<4;row++) for(int col=0;col<5;col++) Box(root,"Book",study+new Vector3(side*2.5f, .45f+row*.58f, .1f+col*.72f),new Vector3(.32f,.42f,.55f),new Color(.20f+row*.08f,.30f+col*.04f,.38f));
            }

            // 办公室：桌椅、双显示器、键盘、台灯
            Vector3 office=h+new Vector3(11.2f,0,5.1f);
            var desk=Box(root,"Office_Desk",office+new Vector3(0,.55f,-1),new Vector3(5,1.1f,1.8f),wood2); desk.AddComponent<GoalBoard>();
            Box(root,"Office_Chair",office+new Vector3(0,.45f,-2.7f),new Vector3(1.4f,.9f,1.3f),fabric);
            Box(root,"Monitor_Left",office+new Vector3(-1.2f,1.55f,-.2f),new Vector3(1.9f,1.2f,.12f),new Color(.08f,.18f,.25f));
            Box(root,"Monitor_Right",office+new Vector3(1.2f,1.55f,-.2f),new Vector3(1.9f,1.2f,.12f),new Color(.08f,.18f,.25f));
            Box(root,"Keyboard",office+new Vector3(0,1.12f,-.8f),new Vector3(1.9f,.08f,.55f),metal);
            Box(root,"Office_Lamp",office+new Vector3(2,1.8f,-.9f),new Vector3(.18f,1,.18f),metal);

            // 卫生间：镜子、洗手池、马桶、淋浴房
            Vector3 bath=h+new Vector3(-10.8f,0,-7.8f);
            Box(root,"Bathroom_Floor",bath,new Vector3(8.5f,.12f,5),new Color(.62f,.65f,.67f));
            Box(root,"Sink",bath+new Vector3(-2.5f,.5f,0),new Vector3(1.6f,1,1.2f),white);
            Box(root,"Mirror",bath+new Vector3(-2.5f,2,1),new Vector3(.15f,1.6f,1.8f),new Color(.65f,.82f,.90f));
            Box(root,"Toilet",bath+new Vector3(.2f,.45f,0),new Vector3(1.5f,.9f,1.9f),white);
            Box(root,"Shower",bath+new Vector3(2.5f,1.2f,0),new Vector3(2,2.4f,2.2f),new Color(.72f,.78f,.80f));
            Box(root,"Shower_Glass",bath+new Vector3(1.5f,1.4f,0),new Vector3(.08f,2.8f,2.4f),glass);

            // 厨房：冰箱、橱柜、灶台、烤箱、水槽、中岛
            Vector3 kitchen=h+new Vector3(10.8f,0,-8);
            Box(root,"Kitchen_Back_Counter",kitchen+new Vector3(0,.6f,2),new Vector3(7,1.2f,1.2f),wood2);
            Box(root,"Kitchen_Island",kitchen+new Vector3(0,.65f,-.4f),new Vector3(5,1.3f,1.8f),wood2);
            Box(root,"Stove",kitchen+new Vector3(-1.9f,1.28f,1.45f),new Vector3(1.8f,.12f,.9f),metal);
            Box(root,"Kitchen_Sink",kitchen+new Vector3(1.7f,1.2f,1.45f),new Vector3(1.4f,.16f,.9f),white);
            Box(root,"Fridge",kitchen+new Vector3(3.4f,1.2f,2),new Vector3(1.6f,2.4f,1.5f),metal);
            Box(root,"Oven",kitchen+new Vector3(-3,1.1f,2),new Vector3(1.4f,2.2f,1.4f),metal);
            Box(root,"Cabinet_1",kitchen+new Vector3(-1.2f,2.1f,2),new Vector3(1.4f,1.2f,1.2f),wood);
            Box(root,"Cabinet_2",kitchen+new Vector3(.3f,2.1f,2),new Vector3(1.4f,1.2f,1.2f),wood);

            // 独立健身区：跑步机、哑铃架、哑铃、深蹲架、杠铃、健身镜
            Vector3 gym=h+new Vector3(8,0,13);
            Box(root,"Gym_Floor",gym,new Vector3(12,.12f,6),new Color(.18f,.20f,.22f));
            Box(root,"Treadmill_Base",gym+new Vector3(-3.2f,.55f,0),new Vector3(3.2f,.45f,1.5f),metal);
            Box(root,"Treadmill_Belt",gym+new Vector3(-3.2f,.83f,0),new Vector3(2.7f,.08f,1.2f),new Color(.05f,.05f,.06f));
            Box(root,"Treadmill_Console",gym+new Vector3(-3.2f,1.7f,.5f),new Vector3(1.1f,.8f,.35f),metal);
            Box(root,"Treadmill_Handle",gym+new Vector3(-3.2f,1.35f,1),new Vector3(.18f,1,.18f),metal);
            Box(root,"Dumbbell_Rack",gym+new Vector3(.2f,.7f,1.7f),new Vector3(4.2f,1,.8f),metal);
            for(int i=0;i<6;i++) Box(root,"Dumbbell_"+i,gym+new Vector3(-1.4f+i*.55f,1.35f,1.7f),new Vector3(.35f,.55f,.8f),metal);
            Box(root,"Squat_Rack_L",gym+new Vector3(3.8f,1.5f,-1.5f),new Vector3(.35f,3,.35f),metal);
            Box(root,"Squat_Rack_R",gym+new Vector3(5.6f,1.5f,-1.5f),new Vector3(.35f,3,.35f),metal);
            Box(root,"Barbell",gym+new Vector3(4.7f,2.6f,-1.5f),new Vector3(2.5f,.18f,.18f),metal);
            Box(root,"Gym_Mirror",gym+new Vector3(0,1.5f,2.85f),new Vector3(5,2.8f,.08f),new Color(.30f,.34f,.38f));

            // 泳池 + 池边躺椅
            Vector3 p=h+new Vector3(6.5f,0,18.2f);
            Box(root,"Pool_Deck",p,new Vector3(15,.10f,8),new Color(.62f,.56f,.48f));
            Box(root,"Pool_Water",p+new Vector3(0,.12f,0),new Vector3(11.5f,.18f,5.2f),pool);
            Box(root,"Pool_Edge",p+new Vector3(-6,.25f,0),new Vector3(.35f,.4f,6),white);
            Box(root,"Pool_Ladder",p+new Vector3(5.2f,.7f,0),new Vector3(.25f,1.4f,1.8f),metal);
            Box(root,"Lounger_1",p+new Vector3(-5,.35f,4),new Vector3(2.5f,.35f,1),white);
            Box(root,"Lounger_2",p+new Vector3(2,.35f,4),new Vector3(2.5f,.35f,1),white);

            // 车库 + 低模汽车 + 工具柜
            Vector3 garage=h+new Vector3(-11.5f,0,-13.8f);
            Box(root,"Garage_Floor",garage,new Vector3(10,.12f,7),new Color(.34f,.35f,.37f));
            Box(root,"Garage_Back",garage+new Vector3(0,1.7f,3.4f),new Vector3(10,3.4f,.25f),wall);
            Box(root,"Garage_Left",garage+new Vector3(-5,1.7f,0),new Vector3(.25f,3.4f,7),wall);
            Box(root,"Garage_Right",garage+new Vector3(5,1.7f,0),new Vector3(.25f,3.4f,7),wall);
            Box(root,"Garage_Door",garage+new Vector3(0,1.6f,-3.35f),new Vector3(8.8f,3,.15f),metal);
            Box(root,"Car_Body",garage+new Vector3(0,.65f,0),new Vector3(5.4f,.8f,2.2f),new Color(.12f,.14f,.17f));
            Box(root,"Car_Cabin",garage+new Vector3(.3f,1.25f,0),new Vector3(2.8f,.8f,1.8f),new Color(.18f,.25f,.30f));
            for(int x=-2;x<=2;x+=4) for(int z=-1;z<=1;z+=2) Box(root,"Car_Wheel",garage+new Vector3(x*.8f,.35f,z*.8f),new Vector3(.65f,.65f,.38f),new Color(.04f,.04f,.05f));
            Box(root,"Tool_Cabinet",garage+new Vector3(-4,1.1f,2),new Vector3(1.3f,2.2f,1.2f),wood);

            // 常驻小猫：身体、头、耳朵、尾巴、猫窝、猫碗。
            Vector3 cat=h+new Vector3(2.5f,0,-4); Color catC=new Color(.62f,.43f,.30f);
            Box(root,"Cat_Bed",cat+new Vector3(0,.18f,0),new Vector3(1.8f,.35f,1.5f),new Color(.48f,.34f,.42f));
            Box(root,"Cat_Body",cat+new Vector3(0,.75f,0),new Vector3(.9f,.65f,1.25f),catC);
            Box(root,"Cat_Head",cat+new Vector3(0,1.35f,.55f),new Vector3(.75f,.75f,.75f),catC);
            Box(root,"Cat_Ear_L",cat+new Vector3(-.25f,1.85f,.55f),new Vector3(.18f,.35f,.18f),catC);
            Box(root,"Cat_Ear_R",cat+new Vector3(.25f,1.85f,.55f),new Vector3(.18f,.35f,.18f),catC);
            Box(root,"Cat_Tail",cat+new Vector3(.55f,.85f,-.75f),new Vector3(.25f,.25f,1),catC);
            Box(root,"Cat_Bowl",cat+new Vector3(1.2f,.12f,.4f),new Vector3(.55f,.18f,.55f),metal);

            // 庭院围栏与绿地
            Box(root,"Garden_Lawn",h+new Vector3(-1,.02f,17),new Vector3(28,.04f,8),green);
            for(int i=-12;i<=12;i+=4){Box(root,"Fence_Post",h+new Vector3(i,.8f,21.2f),new Vector3(.2f,1.6f,.2f),wood);Box(root,"Fence_Rail",h+new Vector3(i+2,.65f,21.2f),new Vector3(4,.18f,.18f),wood);}

            // 超大目标看板：目标 / 计划 / 下一行动 / 关卡路线。
            Vector3 bp=h+new Vector3(1,2.2f,10.72f);
            var board=Box(root,"GoalBoard_LARGE",bp,new Vector3(8.8f,4.8f,.28f),new Color(.06f,.09f,.13f)); board.AddComponent<GoalBoard>();
            Box(root,"Goal_Frame_Top",bp+new Vector3(0,2.5f,0),new Vector3(9.2f,.18f,.34f),new Color(.75f,.52f,.18f));
            Box(root,"Goal_Frame_Bottom",bp+new Vector3(0,-2.5f,0),new Vector3(9.2f,.18f,.34f),new Color(.75f,.52f,.18f));
            Text(root,bp+new Vector3(0,1.55f,-.22f),"MY GOAL / 我的目标",.32f);
            Text(root,bp+new Vector3(0,.55f,-.22f),"PLAN / 当前计划",.25f);
            Text(root,bp+new Vector3(0,-.35f,-.22f),"NEXT ACTION / 下一行动",.23f);
            Text(root,bp+new Vector3(0,-1.25f,-.22f),"LEVEL ROUTE / 关卡路线",.23f);
            var display=root.AddComponent<HomeGoalBoardDisplay>(); display.board=bp;

            Text(root,h+new Vector3(0,3.9f,-11.35f),"我的住处 / LIFE BASE",.10f);
        }

        static GameObject Box(GameObject root,string name,Vector3 pos,Vector3 scale,Color color)
        {
            var go=GameObject.CreatePrimitive(PrimitiveType.Cube); go.name=name; go.transform.SetParent(root.transform,true); go.transform.position=pos; go.transform.localScale=scale;
            var renderer=go.GetComponent<Renderer>(); if(renderer!=null) renderer.sharedMaterial=Mat(name+color.ToString(),color); return go;
        }

        static void Art(GameObject root,Vector3 pos,Vector3 size,Color color,Color frame)
        {
            Box(root,"Art_Frame",pos,new Vector3(size.x+.16f,size.y+.16f,size.z+.04f),frame);
            Box(root,"Art",pos+new Vector3(0,0,-.04f),size,color);
        }

        static void Text(GameObject root,Vector3 pos,string value,float size)
        {
            var go=new GameObject("Home_Text"); go.transform.SetParent(root.transform,true); go.transform.position=pos;
            var tm=go.AddComponent<TextMesh>(); tm.text=value; tm.fontSize=Mathf.RoundToInt(size*100f); tm.characterSize=size; tm.anchor=TextAnchor.MiddleCenter; tm.alignment=TextAlignment.Center; tm.color=Color.white;
        }
    }

    /// <summary>安全安装器：只有 OpenWorldBuilder 已建立 Home_Floor 后才执行。</summary>
    sealed class PlayerHome3DInstaller : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install(){if(GameObject.Find("PlayerHome3DInstaller")!=null)return;new GameObject("PlayerHome3DInstaller").AddComponent<PlayerHome3DInstaller>();}
        IEnumerator Start(){
            for(int i=0;i<180;i++){
                if(OpenWorldBuilder.CityZoneIndex>=0){var floor=GameObject.Find("Home_Floor");if(floor!=null){var h=floor.transform.position;h.y=0;HideLegacy();floor.SetActive(false);PlayerHome3DModel.Build(h);yield break;}}
                yield return new WaitForSeconds(.25f);
            }
        }
        static void HideLegacy(){string[] n={"Bed","Pillow","Wardrobe","Window","Desk","Computer","Phone","Sofa","Screen","Fridge","Counter","Mirror","Sink","Rug"};foreach(var s in n){var g=GameObject.Find(s);if(g!=null)g.SetActive(false);}}
    }

    sealed class HomeGoalBoardDisplay : MonoBehaviour
    {
        public Vector3 board; TextMesh text; float next;
        void Start(){var go=new GameObject("GoalBoard_DynamicStatus");go.transform.SetParent(transform,true);go.transform.position=board+new Vector3(0,0,-.24f);text=go.AddComponent<TextMesh>();text.fontSize=30;text.characterSize=.065f;text.anchor=TextAnchor.MiddleCenter;text.alignment=TextAlignment.Center;text.color=new Color(.88f,.94f,1f);}
        void Update(){if(text==null||Time.time<next)return;next=Time.time+1f;var g=GoalOS.Active;if(g==null){text.text="目标：尚未设置\n计划：进入目标系统创建计划\n下一行动：设置今天最重要的一步\n关卡路线：目标 → 里程碑 → 逆境关卡";return;}var ms=g.CurrentMilestone();string plan=ms!=null?ms.title:"里程碑已完成";var a=new List<string>();foreach(var x in g.criticalActions){if(!x.done)a.Add(x.label);if(a.Count>=2)break;}var r=new List<string>();foreach(var m in g.milestones){r.Add((m.done?"✓":"○")+m.title);if(r.Count>=4)break;}text.text="目标："+g.title+"\n计划："+plan+"\n下一行动："+(a.Count>0?string.Join(" / ",a.ToArray()):"无待办")+"\n路线："+(r.Count>0?string.Join(" → ",r.ToArray()):"尚未生成");}
    }
}

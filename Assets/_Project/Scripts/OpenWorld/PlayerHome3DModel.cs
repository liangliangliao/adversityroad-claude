using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using AdversityRoad.Combat;
using AdversityRoad.Goals;

namespace AdversityRoad.OpenWorld
{
    /// <summary>
    /// 开放城区玩家住宅的完整3D装修模型。
    /// 只在 OpenWorldBuilder 已经建立城区后加载；不会在首页/标题阶段运行。
    /// </summary>
    public static class PlayerHome3DModel
    {
        static readonly Color Wall = new Color(0.78f, 0.76f, 0.71f);
        static readonly Color Floor = new Color(0.42f, 0.29f, 0.19f);
        static readonly Color Wood = new Color(0.30f, 0.20f, 0.13f);
        static readonly Color Wood2 = new Color(0.48f, 0.32f, 0.20f);
        static readonly Color Fabric = new Color(0.35f, 0.43f, 0.56f);
        static readonly Color White = new Color(0.93f, 0.93f, 0.90f);
        static readonly Color Metal = new Color(0.55f, 0.58f, 0.62f);
        static readonly Color Glass = new Color(0.32f, 0.68f, 0.82f);
        static readonly Color Green = new Color(0.25f, 0.48f, 0.30f);
        static readonly Color Pool = new Color(0.10f, 0.55f, 0.78f);

        public static void Build(WorldContext ctx, Vector3 h)
        {
            if (GameObject.Find("PlayerHome_3D_Complete") != null) return;

            var root = new GameObject("PlayerHome_3D_Complete");
            root.transform.position = h;

            const float W = 30f, D = 22f, H = 3.4f;
            ZoneBuilder.Box(ctx, "Home_Floor_3D", h + new Vector3(0, -0.04f, 0), new Vector3(W, 0.22f, D), Floor);

            // 外墙：南侧中央 6m 入口，保持与开放城区步道连通。
            B(ctx, root, "Wall_N", h + new Vector3(0,H/2f,D/2f), new Vector3(W,H,.35f), Wall);
            B(ctx, root, "Wall_W", h + new Vector3(-W/2f,H/2f,0), new Vector3(.35f,H,D), Wall);
            B(ctx, root, "Wall_E", h + new Vector3(W/2f,H/2f,0), new Vector3(.35f,H,D), Wall);
            B(ctx, root, "Wall_S_L", h + new Vector3(-10.5f,H/2f,-D/2f), new Vector3(9,H,.35f), Wall);
            B(ctx, root, "Wall_S_R", h + new Vector3(10.5f,H/2f,-D/2f), new Vector3(9,H,.35f), Wall);
            B(ctx, root, "Wall_S_Top", h + new Vector3(0,3.05f,-D/2f), new Vector3(6,.7f,.35f), Wall);

            // 房间：卧室 / 书房 / 办公室 / 卫生间 / 厨房 / 大客餐厅。
            B(ctx, root, "Partition_Bedroom", h + new Vector3(-6, H/2f,4.8f), new Vector3(.25f,H,12.4f), Wall);
            B(ctx, root, "Partition_Study", h + new Vector3(3.8f,H/2f,7.5f), new Vector3(.25f,H,7f), Wall);
            B(ctx, root, "Partition_Office", h + new Vector3(9,H/2f,1.5f), new Vector3(.25f,H,8f), Wall);
            B(ctx, root, "Partition_Bath", h + new Vector3(-10.5f,H/2f,-5.7f), new Vector3(9,H,.25f), Wall);
            B(ctx, root, "Partition_Kitchen", h + new Vector3(9,H/2f,-7.3f), new Vector3(.25f,H,4.8f), Wall);

            BuildLivingDining(ctx, root, h);
            BuildBedroom(ctx, root, h);
            BuildStudy(ctx, root, h);
            BuildOffice(ctx, root, h);
            BuildBathroom(ctx, root, h);
            BuildKitchen(ctx, root, h);
            BuildGym(ctx, root, h);
            BuildPool(ctx, root, h);
            BuildGarage(ctx, root, h);
            BuildCat(ctx, root, h);
            BuildGarden(ctx, root, h);
            BuildGoalBoard(ctx, root, h);

            ZoneBuilder.AddCeilingLight(h + new Vector3(0,3,-1), new Color(1f,.90f,.72f), 25f);
            ZoneBuilder.AddCeilingLight(h + new Vector3(-10,3,6), new Color(.88f,.93f,1f), 12f);
            ZoneBuilder.AddCeilingLight(h + new Vector3(10,3,5), new Color(.88f,.93f,1f), 12f);
            ZoneBuilder.AddCeilingLight(h + new Vector3(10,3,-8), new Color(1f,.88f,.70f), 14f);

            ZoneBuilder.Decoration(ctx, "Home_Entry_Path", h + new Vector3(0,.06f,-18), new Vector3(6,.06f,14), new Color(.55f,.55f,.57f));
            Sign(h + new Vector3(0,3.9f,-11.35f), "我 的 住 处  ·  LIFE BASE");
        }

        static void BuildLivingDining(WorldContext ctx, GameObject r, Vector3 h)
        {
            B(ctx,r,"Living_Sofa",h+new Vector3(-.5f,.55f,-2),new Vector3(5.8f,1.1f,1.8f),new Color(.38f,.40f,.46f));
            B(ctx,r,"Living_Table",h+new Vector3(-.5f,.32f,.6f),new Vector3(3.4f,.6f,1.4f),Wood2);
            B(ctx,r,"TV",h+new Vector3(-.5f,1.55f,9),new Vector3(5.2f,2.7f,.16f),new Color(.08f,.10f,.13f));
            Art(ctx,r,h+new Vector3(5.3f,2.2f,10.72f),new Vector3(2.8f,1.6f,.1f),new Color(.20f,.43f,.52f));

            B(ctx,r,"Dining_Table",h+new Vector3(5.0f,.55f,-1.5f),new Vector3(5.8f,1.1f,2.4f),Wood2);
            Vector3[] seats={new Vector3(2,.35f,-1.5f),new Vector3(8,.35f,-1.5f),new Vector3(2.8f,.35f,-3.2f),new Vector3(7.2f,.35f,-3.2f),new Vector3(2.8f,.35f,.2f),new Vector3(7.2f,.35f,.2f)};
            for(int i=0;i<seats.Length;i++) B(ctx,r,"Dining_Chair_"+(i+1),h+seats[i],new Vector3(1,.7f,1),Fabric);
            for(int i=0;i<6;i++) B(ctx,r,"Place_Setting_"+(i+1),h+new Vector3(2.9f+(i%2)*4.1f,1.14f,-2.55f+(i/2)*1.0f),new Vector3(.38f,.04f,.38f),White);
        }

        static void BuildBedroom(WorldContext ctx, GameObject r, Vector3 h)
        {
            Vector3 c=h+new Vector3(-10.8f,0,5.4f);
            B(ctx,r,"Bed_Frame",c+new Vector3(0,.35f,.4f),new Vector3(5,.7f,6.6f),Wood);
            B(ctx,r,"Bed_Mattress",c+new Vector3(0,.82f,.4f),new Vector3(4.7f,.28f,6.25f),White);
            B(ctx,r,"Bed_Sheet",c+new Vector3(0,1,-.1f),new Vector3(4.55f,.08f,4.7f),new Color(.78f,.84f,.92f));
            B(ctx,r,"Bed_Blanket",c+new Vector3(0,1.1f,-1.25f),new Vector3(4.55f,.14f,3.15f),Fabric);
            B(ctx,r,"Pillow_Left",c+new Vector3(-1.2f,1.15f,3.05f),new Vector3(2,.26f,1),White);
            B(ctx,r,"Pillow_Right",c+new Vector3(1.2f,1.15f,3.05f),new Vector3(2,.26f,1),White);
            B(ctx,r,"Nightstand_Left",c+new Vector3(-3.8f,.55f,2.3f),new Vector3(1.2f,1.1f,1.2f),Wood2);
            B(ctx,r,"Nightstand_Right",c+new Vector3(3.8f,.55f,2.3f),new Vector3(1.2f,1.1f,1.2f),Wood2);
            Art(ctx,r,c+new Vector3(-4.2f,2.2f,5.2f),new Vector3(2.2f,1.5f,.1f),new Color(.22f,.42f,.54f));
            Art(ctx,r,c+new Vector3(0,2.25f,5.2f),new Vector3(2.4f,1.7f,.1f),new Color(.68f,.38f,.28f));
            Art(ctx,r,c+new Vector3(4.2f,2.2f,5.2f),new Vector3(2.2f,1.5f,.1f),new Color(.35f,.55f,.34f));
            Sign(c+new Vector3(0,3.65f,5),"卧 室  ·  REST");
        }

        static void BuildStudy(WorldContext ctx, GameObject r, Vector3 h)
        {
            Vector3 c=h+new Vector3(-1.6f,0,8.1f);
            B(ctx,r,"Study_Desk",c+new Vector3(0,.55f,-1.4f),new Vector3(4.8f,1.1f,1.6f),Wood2);
            B(ctx,r,"Study_Chair",c+new Vector3(0,.4f,-3),new Vector3(1.2f,.8f,1.2f),Fabric);
            for(int side=-1;side<=1;side+=2)
            {
                B(ctx,r,"Bookcase_"+side,c+new Vector3(side*2.8f,1.5f,2),new Vector3(.7f,3,4.6f),Wood);
                for(int row=0;row<4;row++) for(int col=0;col<5;col++)
                    B(ctx,r,"Book",c+new Vector3(side*2.5f+side*col*.06f,.45f+row*.58f,.1f+col*.72f),new Vector3(.32f,.42f,.55f),new Color(.20f+row*.08f,.30f+col*.04f,.38f));
            }
            Sign(c+new Vector3(0,3.6f,4.1f),"书 房  ·  LEARN");
        }

        static void BuildOffice(WorldContext ctx, GameObject r, Vector3 h)
        {
            Vector3 c=h+new Vector3(11.2f,0,5.1f);
            var desk=B(ctx,r,"Office_Desk",c+new Vector3(0,.55f,-1),new Vector3(5,1.1f,1.8f),Wood2); HomeFixture.Attach(desk,HomeFixtureKind.Desk);
            B(ctx,r,"Office_Chair",c+new Vector3(0,.45f,-2.7f),new Vector3(1.4f,.9f,1.3f),Fabric);
            B(ctx,r,"Monitor_Left",c+new Vector3(-1.2f,1.55f,-.2f),new Vector3(1.9f,1.2f,.12f),new Color(.08f,.18f,.25f));
            B(ctx,r,"Monitor_Right",c+new Vector3(1.2f,1.55f,-.2f),new Vector3(1.9f,1.2f,.12f),new Color(.08f,.18f,.25f));
            B(ctx,r,"Keyboard",c+new Vector3(0,1.12f,-.8f),new Vector3(1.9f,.08f,.55f),Metal);
            B(ctx,r,"Office_Lamp",c+new Vector3(2,1.8f,-.9f),new Vector3(.18f,1,.18f),Metal);
            Art(ctx,r,c+new Vector3(0,2.2f,2.9f),new Vector3(3.2f,1.8f,.1f),new Color(.55f,.35f,.25f));
            Sign(c+new Vector3(0,3.5f,3),"办 公 室  ·  WORK");
        }

        static void BuildBathroom(WorldContext ctx, GameObject r, Vector3 h)
        {
            Vector3 c=h+new Vector3(-10.8f,0,-7.8f);
            B(ctx,r,"Bathroom_Floor",c,new Vector3(8.5f,.12f,5),new Color(.62f,.65f,.67f));
            B(ctx,r,"Sink",c+new Vector3(-2.5f,.5f,0),new Vector3(1.6f,1,1.2f),White);
            var mirror=B(ctx,r,"Mirror",c+new Vector3(-2.5f,2,1),new Vector3(.15f,1.6f,1.8f),new Color(.65f,.82f,.90f)); HomeFixture.Attach(mirror,HomeFixtureKind.Mirror);
            B(ctx,r,"Toilet",c+new Vector3(.2f,.45f,0),new Vector3(1.5f,.9f,1.9f),White);
            B(ctx,r,"Shower",c+new Vector3(2.5f,1.2f,0),new Vector3(2,2.4f,2.2f),new Color(.72f,.78f,.80f));
            B(ctx,r,"Shower_Glass",c+new Vector3(1.5f,1.4f,0),new Vector3(.08f,2.8f,2.4f),Glass);
            Sign(c+new Vector3(0,3.2f,1.9f),"洗 手 间  ·  BATH");
        }

        static void BuildKitchen(WorldContext ctx, GameObject r, Vector3 h)
        {
            Vector3 c=h+new Vector3(10.8f,0,-8);
            B(ctx,r,"Kitchen_Back_Counter",c+new Vector3(0,.6f,2),new Vector3(7,1.2f,1.2f),Wood2);
            B(ctx,r,"Kitchen_Island",c+new Vector3(0,.65f,-.4f),new Vector3(5,1.3f,1.8f),Wood2);
            B(ctx,r,"Stove",c+new Vector3(-1.9f,1.28f,1.45f),new Vector3(1.8f,.12f,.9f),Metal);
            B(ctx,r,"Sink_Kitchen",c+new Vector3(1.7f,1.2f,1.45f),new Vector3(1.4f,.16f,.9f),White);
            var fridge=B(ctx,r,"Fridge",c+new Vector3(3.4f,1.2f,2),new Vector3(1.6f,2.4f,1.5f),Metal); HomeFixture.Attach(fridge,HomeFixtureKind.Fridge);
            B(ctx,r,"Oven",c+new Vector3(-3,1.1f,2),new Vector3(1.4f,2.2f,1.4f),Metal);
            B(ctx,r,"Cabinet_1",c+new Vector3(-1.2f,2.1f,2),new Vector3(1.4f,1.2f,1.2f),Wood);
            B(ctx,r,"Cabinet_2",c+new Vector3(.3f,2.1f,2),new Vector3(1.4f,1.2f,1.2f),Wood);
            Sign(c+new Vector3(0,3.3f,2.6f),"厨 房  ·  HOME COOK");
        }

        static void BuildGym(WorldContext ctx, GameObject r, Vector3 h)
        {
            Vector3 c=h+new Vector3(8,0,13);
            B(ctx,r,"Gym_Floor",c,new Vector3(12,.12f,6),new Color(.18f,.20f,.22f));
            B(ctx,r,"Treadmill_Base",c+new Vector3(-3.2f,.55f,0),new Vector3(3.2f,.45f,1.5f),Metal);
            B(ctx,r,"Treadmill_Belt",c+new Vector3(-3.2f,.83f,0),new Vector3(2.7f,.08f,1.2f),new Color(.05f,.05f,.06f));
            B(ctx,r,"Treadmill_Console",c+new Vector3(-3.2f,1.7f,.5f),new Vector3(1.1f,.8f,.35f),Metal);
            B(ctx,r,"Treadmill_Handle",c+new Vector3(-3.2f,1.35f,1),new Vector3(.18f,1,.18f),Metal);
            B(ctx,r,"Dumbbell_Rack",c+new Vector3(.2f,.7f,1.7f),new Vector3(4.2f,1,.8f),Metal);
            for(int i=0;i<6;i++) B(ctx,r,"Dumbbell_"+i,c+new Vector3(-1.4f+i*.55f,1.35f,1.7f),new Vector3(.35f,.55f,.8f),Metal);
            B(ctx,r,"Squat_Rack_L",c+new Vector3(3.8f,1.5f,-1.5f),new Vector3(.35f,3,.35f),Metal);
            B(ctx,r,"Squat_Rack_R",c+new Vector3(5.6f,1.5f,-1.5f),new Vector3(.35f,3,.35f),Metal);
            B(ctx,r,"Barbell",c+new Vector3(4.7f,2.6f,-1.5f),new Vector3(2.5f,.18f,.18f),Metal);
            B(ctx,r,"Gym_Mirror",c+new Vector3(0,1.5f,2.85f),new Vector3(5,2.8f,.08f),new Color(.30f,.34f,.38f));
            Sign(c+new Vector3(0,3.4f,2.9f),"健 身 房  ·  TRAIN");
        }

        static void BuildPool(WorldContext ctx, GameObject r, Vector3 h)
        {
            Vector3 c=h+new Vector3(6.5f,0,18.2f);
            B(ctx,r,"Pool_Deck",c,new Vector3(15,.10f,8),new Color(.62f,.56f,.48f));
            B(ctx,r,"Pool_Water",c+new Vector3(0,.12f,0),new Vector3(11.5f,.18f,5.2f),Pool);
            B(ctx,r,"Pool_Edge_L",c+new Vector3(-6,.25f,0),new Vector3(.35f,.4f,6),White);
            B(ctx,r,"Pool_Ladder",c+new Vector3(5.2f,.7f,0),new Vector3(.25f,1.4f,1.8f),Metal);
            B(ctx,r,"Pool_Lounger_1",c+new Vector3(-5,.35f,4),new Vector3(2.5f,.35f,1),White);
            B(ctx,r,"Pool_Lounger_2",c+new Vector3(2,.35f,4),new Vector3(2.5f,.35f,1),White);
            Sign(c+new Vector3(0,2.5f,4),"泳 池  ·  RECOVER");
        }

        static void BuildGarage(WorldContext ctx, GameObject r, Vector3 h)
        {
            Vector3 c=h+new Vector3(-11.5f,0,-13.8f);
            B(ctx,r,"Garage_Floor",c,new Vector3(10,.12f,7),new Color(.34f,.35f,.37f));
            B(ctx,r,"Garage_Back",c+new Vector3(0,1.7f,3.4f),new Vector3(10,3.4f,.25f),Wall);
            B(ctx,r,"Garage_Left",c+new Vector3(-5,1.7f,0),new Vector3(.25f,3.4f,7),Wall);
            B(ctx,r,"Garage_Right",c+new Vector3(5,1.7f,0),new Vector3(.25f,3.4f,7),Wall);
            B(ctx,r,"Garage_Door",c+new Vector3(0,1.6f,-3.35f),new Vector3(8.8f,3,.15f),Metal);
            B(ctx,r,"Car_Body",c+new Vector3(0,.65f,0),new Vector3(5.4f,.8f,2.2f),new Color(.12f,.14f,.17f));
            B(ctx,r,"Car_Cabin",c+new Vector3(.3f,1.25f,0),new Vector3(2.8f,.8f,1.8f),new Color(.18f,.25f,.30f));
            for(int x=-2;x<=2;x+=4) for(int z=-1;z<=1;z+=2) B(ctx,r,"Car_Wheel",c+new Vector3(x*.8f,.35f,z*.8f),new Vector3(.65f,.65f,.38f),new Color(.04f,.04f,.05f));
            B(ctx,r,"Tool_Cabinet",c+new Vector3(-4,1.1f,2),new Vector3(1.3f,2.2f,1.2f),Wood);
            Sign(c+new Vector3(0,3.4f,3.2f),"车 库  ·  GARAGE");
        }

        static void BuildCat(WorldContext ctx, GameObject r, Vector3 h)
        {
            Vector3 c=h+new Vector3(2.5f,0,-4);
            Color cat=new Color(.62f,.43f,.30f);
            B(ctx,r,"Cat_Bed",c+new Vector3(0,.18f,0),new Vector3(1.8f,.35f,1.5f),new Color(.48f,.34f,.42f));
            B(ctx,r,"Cat_Body",c+new Vector3(0,.75f,0),new Vector3(.9f,.65f,1.25f),cat);
            B(ctx,r,"Cat_Head",c+new Vector3(0,1.35f,.55f),new Vector3(.75f,.75f,.75f),cat);
            B(ctx,r,"Cat_Ear_L",c+new Vector3(-.25f,1.85f,.55f),new Vector3(.18f,.35f,.18f),cat);
            B(ctx,r,"Cat_Ear_R",c+new Vector3(.25f,1.85f,.55f),new Vector3(.18f,.35f,.18f),cat);
            B(ctx,r,"Cat_Tail",c+new Vector3(.55f,.85f,-.75f),new Vector3(.25f,.25f,1),cat);
            B(ctx,r,"Cat_Bowl",c+new Vector3(1.2f,.12f,.4f),new Vector3(.55f,.18f,.55f),Metal);
            Sign(c+new Vector3(0,2.2f,0),"小 猫");
        }

        static void BuildGarden(WorldContext ctx, GameObject r, Vector3 h)
        {
            B(ctx,r,"Garden_Lawn",h+new Vector3(-1,.02f,17),new Vector3(28,.04f,8),Green);
            for(int i=-12;i<=12;i+=4)
            {
                B(ctx,r,"Fence_Post",h+new Vector3(i,.8f,21.2f),new Vector3(.2f,1.6f,.2f),Wood);
                B(ctx,r,"Fence_Rail",h+new Vector3(i+2,.65f,21.2f),new Vector3(4,.18f,.18f),Wood);
            }
        }

        static void BuildGoalBoard(WorldContext ctx, GameObject r, Vector3 h)
        {
            Vector3 p=h+new Vector3(1.0f,2.2f,10.72f);
            var board=B(ctx,r,"GoalBoard_LARGE",p,new Vector3(8.8f,4.8f,.28f),new Color(.06f,.09f,.13f));
            board.AddComponent<GoalBoard>(); HomeFixture.Attach(board,HomeFixtureKind.GoalBoard);
            B(ctx,r,"Goal_Frame_Top",p+new Vector3(0,2.5f,0),new Vector3(9.2f,.18f,.34f),new Color(.75f,.52f,.18f));
            B(ctx,r,"Goal_Frame_Bottom",p+new Vector3(0,-2.5f,0),new Vector3(9.2f,.18f,.34f),new Color(.75f,.52f,.18f));
            T(r,p+new Vector3(0,1.55f,-.22f),"MY GOAL  ·  我的目标",.32f);
            T(r,p+new Vector3(0,.55f,-.22f),"PLAN  ·  当前计划",.25f);
            T(r,p+new Vector3(0,-.35f,-.22f),"NEXT ACTION  ·  下一行动",.23f);
            T(r,p+new Vector3(0,-1.25f,-.22f),"LEVEL ROUTE  ·  关卡路线",.23f);
            var display=r.AddComponent<HomeGoalBoardDisplay>(); display.board=board;
        }

        static GameObject B(WorldContext ctx, GameObject root, string name, Vector3 p, Vector3 s, Color c)
        {
            var go=ZoneBuilder.Box(ctx,name,p,s,c);
            if(root!=null) go.transform.SetParent(root.transform,true);
            return go;
        }

        static void Art(WorldContext ctx, GameObject root, Vector3 p, Vector3 s, Color c)
        {
            B(ctx,root,"Art_Frame",p,new Vector3(s.x+.16f,s.y+.16f,s.z+.04f),Wood);
            B(ctx,root,"Art",p+new Vector3(0,0,-.04f),s,c);
        }

        static void T(GameObject root, Vector3 p, string text, float size)
        {
            var go=new GameObject("GoalBoard_Label"); go.transform.SetParent(root.transform,true); go.transform.position=p;
            var tm=go.AddComponent<TextMesh>(); tm.text=text; tm.fontSize=Mathf.RoundToInt(size*100f); tm.characterSize=size; tm.anchor=TextAnchor.MiddleCenter; tm.alignment=TextAlignment.Center; tm.color=Color.white;
        }

        static void Sign(Vector3 p, string text)
        {
            var go=new GameObject("Home_Sign"); go.transform.position=p;
            var tm=go.AddComponent<TextMesh>(); tm.text=text; tm.fontSize=52; tm.characterSize=.08f; tm.anchor=TextAnchor.MiddleCenter; tm.alignment=TextAlignment.Center; tm.color=Color.white;
        }
    }

    /// <summary>
    /// 安全安装器：只在开放城区已经建立 Home_Floor 后执行一次。
    /// 首页没有 Home_Floor，因此不会干扰首页启动。
    /// </summary>
    sealed class PlayerHome3DInstaller : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if(GameObject.Find("PlayerHome3DInstaller")!=null) return;
            var go=new GameObject("PlayerHome3DInstaller"); go.AddComponent<PlayerHome3DInstaller>();
        }

        IEnumerator Start()
        {
            for(int i=0;i<180;i++)
            {
                if(OpenWorldBuilder.CityZoneIndex>=0)
                {
                    var floor=GameObject.Find("Home_Floor");
                    if(floor!=null)
                    {
                        Vector3 home=floor.transform.position; home.y=0;
                        HideLegacyFurniture();
                        floor.SetActive(false);
                        var ctx=WorldContext.Active;
                        if(ctx!=null) PlayerHome3DModel.Build(ctx,home);
                        yield break;
                    }
                }
                yield return new WaitForSeconds(.25f);
            }
        }

        static void HideLegacyFurniture()
        {
            string[] names={"Bed","Pillow","Wardrobe","Window","Desk","Computer","Phone","Sofa","Screen","Fridge","Counter","Mirror","Sink","Rug"};
            foreach(var n in names)
            {
                var go=GameObject.Find(n);
                if(go!=null) go.SetActive(false);
            }
        }
    }

    sealed class HomeGoalBoardDisplay : MonoBehaviour
    {
        public GameObject board;
        TextMesh text;
        float next;

        void Start()
        {
            var go=new GameObject("GoalBoard_DynamicStatus"); go.transform.SetParent(transform,true); go.transform.position=board.transform.position+new Vector3(0,0,-.24f);
            text=go.AddComponent<TextMesh>(); text.fontSize=30; text.characterSize=.065f; text.anchor=TextAnchor.MiddleCenter; text.alignment=TextAlignment.Center; text.color=new Color(.88f,.94f,1f);
        }

        void Update()
        {
            if(text==null || Time.time<next) return; next=Time.time+1f;
            var g=GoalOS.Active;
            if(g==null){text.text="目标：尚未设置\n计划：进入目标系统创建计划\n下一行动：设置今天最重要的一步\n关卡路线：目标 → 里程碑 → 逆境关卡";return;}
            var ms=g.CurrentMilestone(); string plan=ms!=null?ms.title:"里程碑已完成";
            var actions=new List<string>(); foreach(var a in g.criticalActions){if(!a.done) actions.Add(a.label); if(actions.Count>=2) break;}
            var route=new List<string>(); foreach(var m in g.milestones){route.Add((m.done?"✓":"○")+m.title); if(route.Count>=4) break;}
            text.text="目标："+g.title+"\n计划："+plan+"\n下一行动："+(actions.Count>0?string.Join(" / ",actions.ToArray()):"无待办")+"\n路线："+(route.Count>0?string.Join(" → ",route.ToArray()):"尚未生成");
        }
    }
}

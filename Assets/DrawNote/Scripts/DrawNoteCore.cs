using Microsoft.MixedReality.Toolkit;
using Microsoft.MixedReality.Toolkit.Input;
using Microsoft.MixedReality.Toolkit.Utilities;
using System;
using System.Collections.Generic;
using UnityEngine;

public class DrawNoteCore : MonoBehaviour
{
    public bool drawing = true; 
    // drawing: 表示当前是否正在绘制。

    // draw targets (create one new draw target while drawing, or for color switch)
    // save seperately to undo and redo draw last
    private Transform drawingsParent;
    public GameObject drawNoteTarget;
    // drawNoteTarget: GameObject类型，表示绘图目标。


    /// <summary>
    /// Default target drawing distance is 64 centimeters away (approx average human arm length) from the User,
    /// DrawPlane GameObject's center is set to 1 meter away with a depth/thickness of 72 centimeters (to more safely catch raycast)
    /// So if the User draws directly in the middle of where their looking, that ray would hit the collider at 64 centimeters away
    /// With the User's arm in a comfortable position the drawing should display just in front of that
    ///默认的目标绘制距离是距离用户64厘米（大约人类平均手臂长度），
    ///DrawPlane GameObject的中心设置为1米远，深度/厚度为72厘米（以更安全地捕捉光线）
    ///因此，如果用户直接在其观看位置的中间绘制，光线将在64厘米外击中对撞机
    ///当用户的手臂处于舒适的位置时，图纸应显示在其正前方
    /// </summary>
    public BoxCollider drawPlane;
    // drawPlane: 表示绘制平面的BoxCollider。


    [SerializeField]
    [Tooltip("Current drawing and all saved drawings.")]
    private List<DrawNoteTargetModel> drawNoteTargets = new List<DrawNoteTargetModel>();
    //drawNoteTargets: 用于存储绘图目标模型的列表。


    [Tooltip("The index location in drawNoteTargets where you are currently or about to draw in.")]
    public int curDrawIndex = 0;
    //curDrawIndex: 当前绘制的索引。


    /// <summary>
    /// Mode to draw notes (Normal, Mesh, etc....)
    /// </summary>
    public DrawNoteType curMode;
    //curMode: 表示当前绘图模式的DrawNoteType枚举变量。

    private MixedRealityPose pose;


    public Material drawMaterial;
    //drawMaterial: 用于绘图的材质。

    public Color[] colorSwatches = new Color[4];
    //colorSwatches: 颜色数组，用于存储可用的颜色。

    public Color drawColor = Color.white;
    //drawColor: 当前使用的颜色。

    public SmallDrawingHUD instanceSmallDrawingHUD;
    // instanceSmallDrawingHUD: SmallDrawingHUD类型，表示一个用于显示绘图设置的HUD。

    // 进行了初始化操作，例如设置可用的颜色选项、设置选定的颜色指示器以及设置HUD的可见性和模式文本
    private void Start()
    {
        instanceSmallDrawingHUD.SetColorBlockOptions(colorSwatches);
        instanceSmallDrawingHUD.SetColorSelectedIndicator(drawColor);
        instanceSmallDrawingHUD.SetVisibility(drawing, true);
        instanceSmallDrawingHUD.SetModeText(curMode.ToString());
    }

    public enum DrawNoteType
    {
        /// <summary>
        /// Draw only on draw plane with hand
        /// </summary>
        Normal,
        /// <summary>
        /// Draw only on meshes with hand
        /// </summary>
        Mesh,
        /// <summary>
        /// Draw from your finger
        /// </summary>
        Finger
    }
    void Update()
    {
        // update transform here intead of parenting gameobject to the camera which MRT gives error 首先，它会更新脚本附加的GameObject的位置和旋转。
        if (CameraCache.Main != null)
        {
            transform.position = CameraCache.Main.transform.position;
            transform.rotation = CameraCache.Main.transform.rotation;
        }

        instanceSmallDrawingHUD.SetVisibility(drawing);

        // show draw plane 然后，根据drawing变量和当前绘图模式设置drawPlane的可见性。
        bool showDrawPlane = false;
        if (drawing && curMode == DrawNoteType.Normal)
        {
            showDrawPlane = true;
        }
        if (drawPlane.enabled != showDrawPlane)
        {
            drawPlane.enabled = showDrawPlane;
        }

        // 最后，如果drawing为true，则调用TryDrawNote()方法进行绘图。
        if (drawing)
        {
            TryDrawNote(curMode);
        }
        else
        {
            if (drawPlane.enabled)
            {
                drawPlane.enabled = false;
            }
        }
    }
    /// <summary>
    /// Try to Draw a Note provided The User's MRTK2 Hand pointer's are hitting the objective hit location for the DrawNoteType specified 尝试在指定的DrawNoteType下绘制一个笔记。
    /// </summary>
    /// <param name="instanceType"></param>
    private void TryDrawNote(DrawNoteType instanceType)
    {
        // confirm any wrist is detected to draw 首先检查手部姿态，如果没有检测到手部姿态，则返回。
        if (HandJointUtils.TryGetJointPose(TrackedHandJoint.Wrist, Handedness.Any, out pose) == false)
        {
            return;
        }

        bool foundDrawPositon = false;
        Vector3 drawPosition = Vector3.zero;

        // if drawing from finger just find the object 接下来，根据当前绘图模式确定绘图位置，并在找到合适的位置后进行绘制。
        if (curMode == DrawNoteType.Finger)
        {
            if (HandJointUtils.TryGetJointPose(TrackedHandJoint.IndexTip, Handedness.Any, out pose))
            {
                foundDrawPositon = true;
                drawPosition = pose.Position;
            }
        }
        else
        {
            // foreach loop below adapted from Julia Schwarz stack overflow 下面的foreach循环改编自Julia Schwarz堆栈溢出
            foreach (var source in CoreServices.InputSystem.DetectedInputSources)
            {
                if (source.SourceType == InputSourceType.Hand)
                {
                    foreach (var p in source.Pointers)
                    {
                        // only get far hand pointers not close 只得到远手指针，不接近
                        if (p is IMixedRealityNearPointer)
                        {
                            continue;
                        }
                        if (p.Result != null)
                        {
                            var startPoint = p.Position;
                            var endPoint = p.Result.Details.Point;
                            var hitObject = p.Result.Details.Object;
                            if (hitObject)
                            {
                                // settings for draw note type
                                if (instanceType == DrawNoteType.Normal)
                                {
                                    if (hitObject.transform.name != "DrawPlane")
                                    {
                                        continue;
                                    }
                                    // redundancy to double check as the draw plane should not be active in the scene 重复检查的冗余，因为绘制平面在场景中不应处于活动状态
                                }
                                else if (instanceType == DrawNoteType.Mesh)
                                {
                                    if (hitObject.transform.name == "DrawPlane")
                                    {
                                        continue;
                                    }
                                }
                                foundDrawPositon = true;
                                drawPosition = endPoint;
                                break;
                            }
                        }
                    }
                }
            }
        }
        // 如果找到了绘制位置
        if (foundDrawPositon)
        {
            // 当绘制目标列表的数量小于或等于当前绘制索引时，创建新的绘制目标对象
            while (drawNoteTargets.Count <= curDrawIndex)
            {
                // 实例化一个新的绘制目标对象
                GameObject instanceDrawTargetGO = Instantiate(drawNoteTarget);
                // 设置新对象的名称
                instanceDrawTargetGO.transform.name = "NewDrawTarget(" + drawNoteTargets.Count + ")";
                // 设置新对象的父对象
                instanceDrawTargetGO.transform.parent = drawingsParent;
                // 创建一个新的材质，并设置绘制颜色
                Material newMat = new Material(drawMaterial);
                newMat.color = drawColor;
                // 将新材质应用于新对象的TrailRenderer组件
                instanceDrawTargetGO.transform.GetChild(0).GetComponent<TrailRenderer>().material = newMat;
                // 创建一个新的绘制目标模型，并将新对象添加到绘制目标列表中
                DrawNoteTargetModel instanceDNTM = new DrawNoteTargetModel(drawNoteTargets.Count, false, instanceDrawTargetGO);
                drawNoteTargets.Add(instanceDNTM);
            }
            // 如果当前绘制索引处的绘制目标对象不为空，则设置该对象的位置为绘制位置
            if (drawNoteTargets[curDrawIndex] != null)
            {
                drawNoteTargets[curDrawIndex].instanceGameObject.transform.position = drawPosition;
            }
        }

    }

    //更新当前绘制颜色并在HUD上更新选定的颜色指示器。
    public void UpdateColor(int setColor)
    {
        // set future color to be picked when making a new Draw Note Target
        drawColor = colorSwatches[setColor];
        // start new drawing if object instance has not been created for this color
        if (curDrawIndex < drawNoteTargets.Count)
        {
            curDrawIndex += 1;
        }
        drawing = true;
        // update seslected color on Small Drawing HUD
        instanceSmallDrawingHUD.SetColorSelectedIndicator(drawColor);
    }
    /// <summary>
    /// Deactivate the most recent drawing 
    /// </summary>

    //用于撤销最近的绘制操作。
    public void Undo()
    {
        if (drawing)
        {
            drawing = false;
            curDrawIndex += 1;
        }
        for (int i = drawNoteTargets.Count - 1; i >= 0; i--)
        {
            if (drawNoteTargets[i].instanceGameObject.activeSelf)
            {
                drawNoteTargets[i].instanceGameObject.SetActive(false);
                // only undo 1 object at a time
                break;
            }
        }
    }
    /// <summary>
    /// Destroy all drawings
    /// </summary>
    public void Clear()
    {
        drawing = false;
        for (int i = 0; i < drawNoteTargets.Count; i++)
        {
            Destroy(drawNoteTargets[i].instanceGameObject);
        }
        drawNoteTargets.Clear();
        curDrawIndex = 0;
    }
    
    //用于在不同的绘图模式之间切换。
    public void SwitchMode()
    {
        curMode++;
        if ((int)curMode >= Enum.GetNames(typeof(DrawNoteType)).Length)
        {
            curMode = 0;
        }
        instanceSmallDrawingHUD.SetModeText(curMode.ToString());
    }
}

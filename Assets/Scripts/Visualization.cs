using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

public class Visualization : MonoBehaviour {

    [SerializeField]
    Shader shader;

    [SerializeField]
    [Range(0, 3000)]
    int count;

    [SerializeField]
    [Range(0, 500)]
    int maxSegmentNum;

    [SerializeField]
    [Range(0, 20)]
    int numOfSides;

    [SerializeField]
    Material mat;

    ComputeBuffer indexBuffer;
    ComputeBuffer vertexBuffer;
    ComputeBuffer linkBuffer;
    ComputeBuffer segmentBuffer;
    ComputeBuffer colorBuf;
    ComputeBuffer initTargetPositionBuffer;

    ComputeBuffer spherePositionBuffer;
    ComputeBuffer argsBuffer;
    uint[] args = new uint[5] { 0, 0, 0, 0, 0 };

    TorusVertex[] vertices;
    Segment[] segments;
    Color[] colors;
    Link[] links;

    Vector3[] spherePositions;
    Vector3[] initTargetPositions;
    int[] indices;

    [SerializeField]
    ComputeShader cs;

    public Bounds bounds;
    public Mesh instanceMesh;
    public Material nodePointMat;

    public float radius { get; set; }
    public float length { get; set; }
    public float height { get; set; }
    public float colorChangeSpeed { get; set; }
    public bool showNodePoint { get; set; }

    [SerializeField]
    Gradient[] gradientColors;

    int totalVertexNum;
    int totalSegmentNum;

    int initSegment_kernelIdx;
    int updateVertex_kernelIdx;
    int applyNoise_kernelIdx;
    int updateTargetPosition_kernelIdx;

    List<Color[]> colorsArr;

    public Earth earth;

    Vector3[] arrivePoints;
    Vector3[] destPoints;
    Vector3[] points;

    [SerializeField]
    bool drawOnRandomPoint;

    // Use this for initialization
    void Start ()
    {
        if(drawOnRandomPoint == false)
        {
            earth.PreProcessingForSampling();
            arrivePoints = earth.GetArrivePositions(count);
            destPoints = earth.GetDestPositions(count);
        }
        else
        {
            arrivePoints = earth.GetRandomPositions(count);
            destPoints = earth.GetRandomPositions(count);
        }

        DrawLine();
    }

    void DrawLine()
    {
        height = 1.0f;
        radius = 0.09f;
        colorChangeSpeed = 0.1f;

        InitKernelIndex();

        totalSegmentNum = (count * (maxSegmentNum + 1));
        totalVertexNum = count * (numOfSides * maxSegmentNum + numOfSides);
        indices = new int[3 * 2 * numOfSides * maxSegmentNum];

        Debug.Log("totalVertexNum : " + totalVertexNum);
        Debug.Log("totalSegmentNum : " + totalSegmentNum);
        Debug.Log("indexNum : " + indices.Length);

        vertices = new TorusVertex[totalVertexNum];
        segments = new Segment[totalSegmentNum];
        links = new Link[count];
        initTargetPositions = new Vector3[count];
        spherePositions = new Vector3[count * 2];

        for (int i = 0; i < totalSegmentNum; i++)
            segments[i] = new Segment();

        for (int i = 0; i < totalVertexNum; i++)
            vertices[i] = new TorusVertex();

        for (int i = 0; i < count; i++)
        {
            var fromPos = arrivePoints[i];
            var toPos = destPoints[i];

            links[i].fromPos = fromPos;
            links[i].toPos = toPos;

            initTargetPositions[i] = toPos;

            spherePositions[i * 2] = fromPos;
            spherePositions[i * 2 + 1] = toPos;
        }

        SetIndices();
        InitBuffer();
        UpdateSegments();

        colorsArr = new List<Color[]>();

        for (int i = 0; i < gradientColors.Length; i++)
        {
            Gradient gradient = gradientColors[i];

            var colors = new Color[100];
            for (int j = 0; j < 100; j++)
            {
                colors[j] = gradient.Evaluate(j / 100f);
            }

            colorsArr.Add(colors);
        }
    }

    void ChangeColor(float t)
    {
        int fromIndex = (int)t;
        int toIndex = (int)t + 1 >= colorsArr.Count ? 0 : (int)t + 1;
        float tt = t % 1.0f;
        colors = LerpColors(colorsArr[fromIndex], colorsArr[toIndex], tt);
    }
    Color[] LerpColors(Color[] from, Color[] to, float t)
    {
        Color[] colors = new Color[from.Length];

        for (int i = 0; i < from.Length; i++)
        {
            colors[i] = Color.Lerp(from[i], to[i], t);
        }

        return colors;
    }


    private void InitKernelIndex()
    {
        initSegment_kernelIdx = cs.FindKernel("InitSegment");
        updateVertex_kernelIdx = cs.FindKernel("UpdateVertex");
        applyNoise_kernelIdx = cs.FindKernel("ApplyNoise");
        updateTargetPosition_kernelIdx = cs.FindKernel("UpdateTargetPosition");
    }

    private void SetIndices()
    {
        int lastVertexIndex = 0;
        int v_count = 0;
        for (int i = 0; i <= maxSegmentNum; i++)
        {
            v_count = (i + 1) * numOfSides;

            if (i > 0)
            {
                for (var currentRingVertexIndex = v_count - numOfSides; currentRingVertexIndex < v_count; currentRingVertexIndex++)
                {
                    var p00 = (lastVertexIndex + 1) >= v_count - numOfSides ? v_count - (numOfSides * 2) : (lastVertexIndex + 1);
                    var p01 = (lastVertexIndex);
                    var p02 = (currentRingVertexIndex);

                    var ii = lastVertexIndex * 6;

                    indices[ii + 0] = p00; // Triangle A
                    indices[ii + 1] = p01;
                    indices[ii + 2] = p02;

                    var p10 = currentRingVertexIndex;
                    var p11 = (currentRingVertexIndex + 1) >= v_count ? v_count - numOfSides : currentRingVertexIndex + 1;
                    var p12 = lastVertexIndex + 1 >= v_count - numOfSides ? v_count - (numOfSides * 2) : (lastVertexIndex + 1);

                    indices[ii + 3] = p10; // Triangle B
                    indices[ii + 4] = p11;
                    indices[ii + 5] = p12;

                    lastVertexIndex++;
                }
            }
        }
    }

    private void InitBuffer()
    {
        argsBuffer = new ComputeBuffer(1, args.Length * sizeof(uint), ComputeBufferType.IndirectArguments);
        uint numIndices = (instanceMesh != null) ? (uint)instanceMesh.GetIndexCount(0) : 0;
        args[0] = numIndices;
        args[1] = (uint)count * 2;
        argsBuffer.SetData(args);

        vertexBuffer = new ComputeBuffer(vertices.Length, Marshal.SizeOf(typeof(TorusVertex)));
        vertexBuffer.SetData(vertices);

        indexBuffer = new ComputeBuffer(indices.Length, sizeof(int));
        indexBuffer.SetData(indices);

        segmentBuffer = new ComputeBuffer(segments.Length, Marshal.SizeOf(typeof(Segment)));
        segmentBuffer.SetData(segments);

        linkBuffer = new ComputeBuffer(count, Marshal.SizeOf(typeof(Link)));
        linkBuffer.SetData(links);

        colorBuf = new ComputeBuffer(count, Marshal.SizeOf(typeof(Color)));

        spherePositionBuffer = new ComputeBuffer(count * 2, Marshal.SizeOf(typeof(Vector3)));
        spherePositionBuffer.SetData(spherePositions);

        initTargetPositionBuffer = new ComputeBuffer(count, Marshal.SizeOf(typeof(Vector3)));
        initTargetPositionBuffer.SetData(initTargetPositions);
    }

    private void UpdateSegments()
    {
        cs.SetFloat("_MaxSegment", maxSegmentNum);
        cs.SetBuffer(initSegment_kernelIdx, "_LinkBuffer", linkBuffer);
        cs.SetBuffer(initSegment_kernelIdx, "_SegmentBuffer", segmentBuffer);
        cs.Dispatch(initSegment_kernelIdx, count / 8 + 1, 1, 1);
    }

    private void OnRenderObject()
    {
        UpdateSegments();

        var color_t = Mathf.Repeat(Time.time * colorChangeSpeed, gradientColors.Length);
        ChangeColor(color_t);
        colorBuf.SetData(colors);

        cs.SetFloat("_MaxSegment", maxSegmentNum);
        cs.SetFloat("_NumOfSlide", numOfSides);
        cs.SetFloat("_Time", Time.time);
        cs.SetFloat("_Radius", radius);

        cs.SetFloat("_Height", height);
        cs.SetBuffer(applyNoise_kernelIdx, "_SegmentBuffer", segmentBuffer);
        cs.SetBuffer(applyNoise_kernelIdx, "_LinkBuffer", linkBuffer);
        cs.Dispatch(applyNoise_kernelIdx, totalSegmentNum / 8 + 1, 1, 1);

        cs.SetBuffer(updateVertex_kernelIdx, "_VertexBuffer", vertexBuffer);
        cs.SetBuffer(updateVertex_kernelIdx, "_SegmentBuffer", segmentBuffer);
        cs.Dispatch(updateVertex_kernelIdx, totalVertexNum / 8 + 1, 1, 1);

        cs.SetBuffer(updateTargetPosition_kernelIdx, "_InitTargetPosition", initTargetPositionBuffer);
        cs.SetBuffer(updateTargetPosition_kernelIdx, "_SpherePositions", spherePositionBuffer);
        cs.SetBuffer(updateTargetPosition_kernelIdx, "_LinkBuffer", linkBuffer);
        cs.Dispatch(updateTargetPosition_kernelIdx, count * 2 / 8 + 1, 1, 1);

        mat.SetPass(0);
        mat.SetBuffer("_IndexBuffer", indexBuffer);
        mat.SetBuffer("_VertexBuffer", vertexBuffer);
        mat.SetBuffer("_ColorBuffer", colorBuf);
        mat.SetFloat("_MaxSegment", maxSegmentNum);
        mat.SetInt("_NumVertexOfPerTorus", vertices.Length / count);
        mat.SetInt("_NumIndexOfPerTorus", indices.Length);

        Graphics.DrawProcedural(MeshTopology.Triangles, indices.Length, count);
    }

    void Update()
    {
        nodePointMat.SetFloat("_ShowNodePoint", showNodePoint == true ? 1 : 0);
        nodePointMat.SetBuffer("positionBuffer", spherePositionBuffer);
        Graphics.DrawMeshInstancedIndirect(instanceMesh, 0, nodePointMat, bounds, argsBuffer);
    }

    private void OnDrawGizmos()
    {
        return;

        if (links == null)
            return;

        for (int i = 0; i < links.Length; i++)
        {
            var initPos = links[i];

            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(initPos.fromPos, 2.0f);
        }

        if (segments == null)
            return;

        for (int i = 0; i < segments.Length; i++)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(segments[i].pos, 1.0f);
            Gizmos.DrawRay(segments[i].pos, segments[i].normal * 10);
        }
    }

    struct TorusVertex
    {
        public Vector3 pos;
        public Vector3 normal;
        public Vector2 uv;
    };

    struct Segment
    {
        public int index;
        public Vector3 initPos;
        public Vector3 pos;
        public Vector3 direction;
        public Vector3 normal;
    };

    struct Link
    {
        public Vector3 fromPos;
        public Vector3 toPos;
    }
}

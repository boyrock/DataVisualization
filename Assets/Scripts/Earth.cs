using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Earth : MonoBehaviour {

    MeshFilter mf;

    [SerializeField]
    Texture2D targetPointMaskTex;

    int triangle_totalCount;
    Mesh mesh;

    float arrive_totalProbability;
    float dest_totalProbability;

    float[] sizeOfTriangles;

    TriangleData[] arrive_probalbilityArr;
    TriangleData[] dest_probalbilityArr;

    private void Awake()
    {
        mf = this.GetComponent<MeshFilter>();
        mesh = mf.mesh;
    }
    // Use this for initialization
    void Start () {

    }

    public void PreProcessingForSampling()
    {
        triangle_totalCount = mf.mesh.triangles.Length / 3;

        sizeOfTriangles = new float[triangle_totalCount];

        CalcuteTotalProbability();

        CalcuProbability();
    }
        // Update is called once per frame
    void Update () {
		
	}

    void CalcuProbability()
    {
        arrive_probalbilityArr = new TriangleData[triangle_totalCount];
        dest_probalbilityArr = new TriangleData[triangle_totalCount];

        for (int i = 0; i < triangle_totalCount; i++)
        {
            var t0 = mesh.triangles[i * 3];
            var t1 = mesh.triangles[i * 3 + 1];
            var t2 = mesh.triangles[i * 3 + 2];

            var uv1 = mesh.uv[t0];
            var uv2 = mesh.uv[t1];
            var uv3 = mesh.uv[t2];

            float size = sizeOfTriangles[i];

            Color color = GetColorFromTex(uv1, uv2, uv3);
            float arrive_density = color.r;
            float dest_density = color.b;

            SetProbability(i, size, arrive_totalProbability, arrive_density, arrive_probalbilityArr);
            SetProbability(i, size, dest_totalProbability, dest_density, dest_probalbilityArr);
        }
    }

    private void SetProbability(int i, float size, float totalProbability, float density, TriangleData[] array)
    {
        TriangleData td = new TriangleData();

        float pdf = (density * size) / totalProbability;
        td.index = i;
        td.pdf = pdf;

        for (int j = 0; j < i; j++)
        {
            td.cdf += array[j].pdf;
        }

        td.cdf += pdf;

        array[i] = td;
    }

    public Vector3[] GetArrivePositions(int samplingCount)
    {
        return ChoiceRandomPoint(samplingCount, arrive_probalbilityArr);
    }

    public Vector3[] GetDestPositions(int samplingCount)
    {
        return ChoiceRandomPoint(samplingCount, dest_probalbilityArr);
    }

    public Vector3[] GetRandomPositions(int samplingCount)
    {
        Vector3[] points = new Vector3[samplingCount];

        for (int i = 0; i < points.Length; i++)
        {
            var p = GetRandomPointOnSurface();
            points[i] = p;
        }

        return points;
    }

    private Vector3[] ChoiceRandomPoint(int samplingCount, TriangleData[] probalbilityArr)
    {
        var pointArr = new Vector3[samplingCount];

        for (int i = 0; i < samplingCount; i++)
        {
            Vector3 point = new Vector3();

            var triangle_index = Bisection(probalbilityArr, Random.value);

            if (triangle_index.HasValue)
            {
                var rnd1 = Random.value;
                var rnd2 = Random.value;

                var u = 1 - Mathf.Sqrt(rnd1);
                var v = rnd2 * Mathf.Sqrt(rnd1);

                point = UvToWorldPosition(new Vector2(u, v), triangle_index.Value);

                pointArr[i] = point;
            }
        }

        return pointArr;
    }
    public int? Bisection(TriangleData[] array, float target)
    {
        if (array == null || array.Length == 0)
            return null;

        int left = 0;
        int right = array.Length - 1;

        while (left <= right)
        {
            float d = (right - left) / 2.0f;
            if (d <= 0.5f)
                return right;

            int middle = left + (right - left) / 2;

            if (array[middle].cdf > target)
            {
                right = middle;
            }
            else if (array[middle].cdf < target)
            {
                left = middle;
            }
            else
            {
                return middle;
            }
        }

        Debug.Log("Seek Fail!!");
        return null;
    }

    private Vector3 GetRandomPointOnSurface()
    {
        var verts = mesh.vertices;
        var indexCount = mesh.triangles.Length / 3;
        var index = Random.Range(0, indexCount);

        var rand01 = Random.value;
        var rand02 = Random.value;

        var u = 1 - Mathf.Sqrt(rand01);
        var v = rand02 * Mathf.Sqrt(rand01);

        return UvToWorldPosition(new Vector2(u, v), index);
    }

    Vector3 UvToWorldPosition(Vector2 uv, int index)
    {
        var t1 = mf.mesh.triangles[index * 3];
        var t2 = mf.mesh.triangles[index * 3 + 1];
        var t3 = mf.mesh.triangles[index * 3 + 2];

        var u = uv.x;
        var v = uv.y;

        float aa = u;
        float bb = v;
        float cc = 1 - u - v;

        Vector3 p3D = aa * mf.mesh.vertices[t1] + bb * mf.mesh.vertices[t2] + cc * mf.mesh.vertices[t3];

        return transform.TransformPoint(p3D);
    }

    void CalcuteTotalProbability()
    {
        for (int i = 0; i < triangle_totalCount; i++)
        {
            var t1 = mesh.triangles[i * 3];
            var t2 = mesh.triangles[i * 3 + 1];
            var t3 = mesh.triangles[i * 3 + 2];

            var v1 = mesh.vertices[t1];
            var v2 = mesh.vertices[t2];
            var v3 = mesh.vertices[t3];

            var size = GetTriangleSize(v1, v2, v3);

            sizeOfTriangles[i] = size;

            var uv1 = mesh.uv[t1];
            var uv2 = mesh.uv[t2];
            var uv3 = mesh.uv[t3];
            
            Color color = GetColorFromTex(uv1, uv2, uv3);

            float arrive_density = color.r;
            float dest_density = color.b;

            arrive_totalProbability += (arrive_density * size);
            dest_totalProbability += (dest_density * size);
        }
    }

    private float GetTriangleSize(Vector3 v1, Vector3 v2, Vector3 v3)
    {
        Vector3 v = Vector3.Cross(v1 - v2, v1 - v3);
        float size = v.magnitude * 0.5f;
        return Mathf.Abs(size);
    }
    private Vector2 GetBarycentric(Vector2 uv1, Vector2 uv2, Vector2 uv3)
    {
        Vector2 bary;
        bary = (uv1 + uv2 + uv3) / 3.0f;

        return bary;
    }
    private Color GetColorFromTex(Vector2 uv1, Vector2 uv2, Vector2 uv3)
    {
        Vector2 barycentricPos = GetBarycentric(uv1, uv2, uv3);

        if (targetPointMaskTex != null)
        {
            Color c = targetPointMaskTex.GetPixel((int)(barycentricPos.x * targetPointMaskTex.width), (int)(barycentricPos.y * targetPointMaskTex.height));
            return c;
        }

        return Color.black;
    }
}

public struct TriangleData
{
    public int index;
    public float pdf;
    public float cdf;
}

public enum AA
{
    Source,
    Destination
}

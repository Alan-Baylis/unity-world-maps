﻿//#define PAINT_QUADS

using UnityEngine;
using System.Collections;
using System.Collections.Generic;


// This [ExecuteInEditMode] is for Start to execute and then display the right mesh
// created with PlanesFactory. 
[ExecuteInEditMode]
public class QuadtreeLODPlane : MonoBehaviour {
	public int vertexResolution = 20;

	private OnlineTexture onlineTexture;

	private string nodeID = "0";

	GameObject[] children_ = null;

	private int depth_ = 0;
	const int MAX_DEPTH = 7;


	public void Start()
	{
		if (depth_ == 0) {
			Vector3 meshSize = GetComponent<MeshRenderer> ().bounds.size;
			if (meshSize.x != meshSize.z) {
				Debug.LogWarning ("LOD plane must be square (currently: " +
				meshSize.x +
				"x" +
				meshSize.y +
				")");
			}

			float mapSize = Mathf.Max (meshSize.x, meshSize.z);

			if (GetComponent<WMSTexture> () != null) {
				onlineTexture = this.GetComponent<WMSTexture> ();

				if (GetComponent<BingMapsTexture> () != null) {
					throw new UnityException ("Terrain can't have both WMSTexture and BingMapsTexture componets!");
				}
			} else if (GetComponent<BingMapsTexture> () != null) {
				onlineTexture = this.GetComponent<BingMapsTexture> ();
			} else {
				throw new MissingComponentException ("Terrain must have a WMSTexture or BingMapsTexture");
			}

			nodeID = "0";

			// Create the root mesh.
			gameObject.GetComponent<MeshFilter> ().mesh = PlanesFactory.CreateHorizontalPlane (mapSize, vertexResolution);
		}

		if (Application.isPlaying) {
			onlineTexture.RequestTexture (nodeID);
		}
	}


	public GameObject CreateChild( string nodeID, Color color, Vector3 localPosition )
	{
		// Create the child game object.
		// Initially this was done by duplicating current game object, but this copied
		// children as well and errors arisen.
		GameObject childGameObject = new GameObject();
		childGameObject.AddComponent<MeshRenderer>();
		childGameObject.AddComponent<MeshFilter>();
		childGameObject.AddComponent<QuadtreeLODPlane>();

		// Copy parent's mesh
		Mesh parentMesh = gameObject.GetComponent<MeshFilter>().mesh;
		Mesh childMesh = new Mesh ();
		childMesh.vertices = parentMesh.vertices;
		childMesh.triangles = parentMesh.triangles;
		childMesh.uv = parentMesh.uv;
		childMesh.RecalculateNormals ();
		childMesh.RecalculateBounds ();
		childGameObject.GetComponent<MeshFilter> ().mesh = childMesh;

		// Make this child transform relative to parent.
		childGameObject.transform.parent = gameObject.transform;

		// Previous assignment alters local transformation, so we reset it.
		childGameObject.transform.localRotation = Quaternion.identity;
		childGameObject.transform.localPosition = localPosition;

		childGameObject.transform.localScale = new Vector3( 0.5f, 1.0f, 0.5f );

		// Create material
		childGameObject.GetComponent<Renderer> ().material = new Material (GetComponent<Renderer>().material.shader);

		#if PAINT_QUADS
			childGameObject.GetComponent<Renderer>().material.color = color;
		#endif
		childGameObject.GetComponent<QuadtreeLODPlane>().depth_ = this.depth_ + 1;
		childGameObject.GetComponent<QuadtreeLODPlane>().vertexResolution = this.vertexResolution;
		childGameObject.GetComponent<QuadtreeLODPlane> ().nodeID = nodeID;


		if (gameObject.GetComponent<WMSTexture> () != null) {
			childGameObject.GetComponent<QuadtreeLODPlane>().onlineTexture = childGameObject.AddComponent<WMSTexture> ();
		} else {
			childGameObject.GetComponent<QuadtreeLODPlane>().onlineTexture = childGameObject.AddComponent<BingMapsTexture> ();
		}
		onlineTexture.CopyTo (childGameObject.GetComponent<QuadtreeLODPlane>().onlineTexture);
		childGameObject.GetComponent<QuadtreeLODPlane>().SetVisible (false);

		return childGameObject;
	}


	private void FlipUV()
	{
		Vector2[] uv = gameObject.GetComponent<MeshFilter> ().mesh.uv;
		for (int i=0; i<uv.Length; i++) {
			uv[i].x = 1.0f - uv[i].x;
			uv[i].y = 1.0f - uv[i].y;
		}
		gameObject.GetComponent<MeshFilter> ().mesh.uv = uv;
	}


	public void SetVisible( bool visible )
	{
		// Set node visibility.
		gameObject.GetComponent<MeshRenderer> ().enabled = visible;


		// Enable or disable collider according to new visibility value.
		Collider collider = gameObject.GetComponent<Collider>();
		if ( collider != null ) {
			collider.enabled = visible;
		}

		// No matter which visibility is applied to this node, children
		// visibility must be set to false.
		if (children_ != null) {
			for( int i = 0; i < children_.Length; i++ ){
				children_[i].GetComponent<QuadtreeLODPlane>().SetVisible (false);
			}
		}
	}


	void Update () {
		// Don't Update in edit mode.
		if( !Application.isPlaying ){
			return;
		}

		if (Visible() || AreChildrenLoaded()) {
			DistanceTestResult distanceTestResult = DoDistanceTest();
			Vector3 meshSize = Vector3.Scale (GetComponent<MeshFilter>().mesh.bounds.size, gameObject.transform.lossyScale);

			// Subdivide the plane if camera is closer than a threshold.
			if (Visible() && distanceTestResult == DistanceTestResult.SUBDIVIDE ) {
				// Create children if they don't exist.
				if (depth_ < MAX_DEPTH && children_ == null) {
					CreateChildren (meshSize);
				}

				// Make this node invisible and children visible.
				if (AreChildrenLoaded ()) {
					SetVisible (false);
					for (int i = 0; i < children_.Length; i++) {
						children_ [i].GetComponent<QuadtreeLODPlane>().SetVisible (true);
					}
				}
			}else if ( !Visible() && AreChildrenLoaded () && ParentOfVisibleNodes() && distanceTestResult == DistanceTestResult.JOIN ) {
				SetVisible (true);
				for (int i = 0; i < children_.Length; i++) {
					children_ [i].GetComponent<QuadtreeLODPlane>().SetVisible (false);
				}
			}
		}
	}


	enum DistanceTestResult 
	{
		DO_NOTHING,
		SUBDIVIDE,
		JOIN
	}


	private DistanceTestResult DoDistanceTest()
	{
		const float THRESHOLD_FACTOR = 2.5f;

		Vector3 cameraPos = Camera.main.transform.position;
		float distanceCameraBorder = Vector3.Distance (cameraPos, gameObject.GetComponent<MeshRenderer> ().bounds.ClosestPoint (cameraPos));
		Vector3 boundsSize = gameObject.GetComponent<MeshRenderer> ().bounds.size;
		float radius = (boundsSize.x + boundsSize.y + boundsSize.z) / 3.0f;

		if (distanceCameraBorder < THRESHOLD_FACTOR * radius) {
			return DistanceTestResult.SUBDIVIDE;
		} else if (distanceCameraBorder >= THRESHOLD_FACTOR * radius) {
			return DistanceTestResult.JOIN;
		}

		return DistanceTestResult.DO_NOTHING;
	}


	private void CreateChildren( Vector3 meshSize )
	{
		Vector3 S = new Vector3(
			1.0f / gameObject.transform.lossyScale.x,
			1.0f / gameObject.transform.lossyScale.y,
			1.0f / gameObject.transform.lossyScale.z
		);


		Vector3[] childLocalPosition = new Vector3[]
		{
			Vector3.Scale ( new Vector3( -meshSize.x/4,0,meshSize.z/4 ), S ),
			Vector3.Scale ( new Vector3( -meshSize.x/4,0,-meshSize.z/4 ), S ),
			Vector3.Scale ( new Vector3( meshSize.x/4,0,meshSize.z/4), S ),
			Vector3.Scale ( new Vector3( meshSize.x/4,0,-meshSize.z/4), S )
		};


		Color[] childrenColors = new Color[]
		{
			new Color( 1.0f, 0.0f, 0.0f, 1.0f ),
			new Color( 0.0f, 1.0f, 0.0f, 1.0f ),
			new Color( 0.0f, 0.0f, 1.0f, 1.0f ),
			new Color( 1.0f, 1.0f, 0.0f, 1.0f )
		};


		string[] childrenIDs = new string[] 
		{
			nodeID + "0",
			nodeID + "1",
			nodeID + "2",
			nodeID + "3"
		};

		children_ = new GameObject[]{ null, null, null, null };
		for( int i=0; i<4; i++ ){
			children_[i] = CreateChild( childrenIDs[i], childrenColors[i], childLocalPosition[i] );
		}
	}


	private bool AreChildrenLoaded(){
		if (children_ != null) {
			for (int i = 0; i < 4; i++) {
				if (children_ [i].GetComponent<QuadtreeLODPlane>().onlineTexture.textureLoaded == false) {
					return false;
				}
			}
			return true;
		} else {
			return false;
		}
	}


	public bool Visible()
	{
		return gameObject.GetComponent<MeshRenderer> ().enabled;
	}


	public bool ParentOfVisibleNodes()
	{
		if (children_ == null) {
			return false;
		}
		for (int i = 0; i < children_.Length; i++) {
			if (children_ [i].GetComponent<QuadtreeLODPlane> ().Visible () == false) {
				return false;
			}
		}
		return true;
	}
}

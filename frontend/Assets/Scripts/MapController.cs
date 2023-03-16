using UnityEngine;
using System;
using shared;
using static shared.Battle;

public class MapController : MonoBehaviour {
    int roomCapacity = 2;
    int renderFrameId; // After battle started
    int chaserRenderFrameId; // at any moment, "chaserRenderFrameId <= renderFrameId", but "chaserRenderFrameId" would fluctuate according to "onInputFrameDownsyncBatch"
    int maxChasingRenderFramesPerUpdate;
    int renderBufferSize;
    public GameObject characterPrefab;

    int[] lastIndividuallyConfirmedInputFrameId;
    ulong[] lastIndividuallyConfirmedInputList;
    PlayerDownsync selfPlayerInfo = null;
    FrameRingBuffer<RoomDownsyncFrame> renderBuffer = null;
    FrameRingBuffer<InputFrameDownsync> inputBuffer = null;

    ulong[] prefabbedInputListHolder;
    GameObject[] playersArr;

    int spaceOffsetX;
    int spaceOffsetY;

    shared.Collision collisionHolder;
    Vector[] effPushbacks;
    Vector[][] hardPushbackNormsArr;
    bool[] jumpedOrNotList;
    shared.Collider[] dynamicRectangleColliders;
    CollisionSpace collisionSys;

    // Start is called before the first frame update
    void Start() {
        _resetCurrentMatch();
        spawnPlayerNode(0, spaceOffsetX, -spaceOffsetY);
		var camOldPos = Camera.main.transform.position; 
		Camera.main.transform.position = new Vector3(spaceOffsetX, -spaceOffsetY, camOldPos.z); 
    }

    // Update is called once per frame
    void Update() {
        int noDelayInputFrameId = ConvertToNoDelayInputFrameId(renderFrameId);
        ulong prevSelfInput = 0, currSelfInput = 0;
        if (ShouldGenerateInputFrameUpsync(renderFrameId)) {
            (prevSelfInput, currSelfInput) = getOrPrefabInputFrameUpsync(noDelayInputFrameId, true, prefabbedInputListHolder);
        }
        int delayedInputFrameId = ConvertToDelayedInputFrameId(renderFrameId);
        var (delayedInputFrameExists, _) = inputBuffer.GetByFrameId(delayedInputFrameId);
        if (!delayedInputFrameExists) {
            // Possible edge case after resync, kindly note that it's OK to prefab a "future inputFrame" here, because "sendInputFrameUpsyncBatch" would be capped by "noDelayInputFrameId from self.renderFrameId". 
            getOrPrefabInputFrameUpsync(delayedInputFrameId, false, prefabbedInputListHolder);
        }

        int prevChaserRenderFrameId = chaserRenderFrameId;
        int nextChaserRenderFrameId = (prevChaserRenderFrameId + maxChasingRenderFramesPerUpdate);
        if (nextChaserRenderFrameId > renderFrameId) {
            nextChaserRenderFrameId = renderFrameId;
        }
        if (prevChaserRenderFrameId < nextChaserRenderFrameId) {
            // Do not execute "rollbackAndChase" when "prevChaserRenderFrameId == nextChaserRenderFrameId", otherwise if "nextChaserRenderFrameId == self.renderFrameId" we'd be wasting computing power once. 
            rollbackAndChase(prevChaserRenderFrameId, nextChaserRenderFrameId, collisionSys, true);
        }

        // Inside the following "rollbackAndChase" actually ROLLS FORWARD w.r.t. the corresponding delayedInputFrame, REGARDLESS OF whether or not "chaserRenderFrameId == renderFrameId" now. 
        var (prevRdf, rdf) = rollbackAndChase(renderFrameId, renderFrameId + 1, collisionSys, false);
        // Having "prevRdf.Id == renderFrameId" & "rdf.Id == renderFrameId+1" 

        applyRoomDownsyncFrameDynamics(rdf, prevRdf);
        ++renderFrameId;
    }

    void spawnPlayerNode(int joinIndex, int vx, int vy) {
        GameObject newPlayerNode = Instantiate(characterPrefab, new Vector3(vx, vy, 0), Quaternion.identity);
        playersArr[joinIndex] = newPlayerNode;
    }

    (ulong, ulong) getOrPrefabInputFrameUpsync(int inputFrameId, bool canConfirmSelf, ulong[] prefabbedInputList) {
        if (null == selfPlayerInfo) {
            String msg = String.Format("noDelayInputFrameId={0:D} couldn't be generated due to selfPlayerInfo being null", inputFrameId);
            throw new ArgumentException(msg);
        }

        ulong previousSelfInput = 0,
          currSelfInput = 0;
        int joinIndex = selfPlayerInfo.JoinIndex;
        ulong selfJoinIndexMask = ((ulong)1 << (joinIndex - 1));
        var (_, existingInputFrame) = inputBuffer.GetByFrameId(inputFrameId);
        var (_, previousInputFrameDownsync) = inputBuffer.GetByFrameId(inputFrameId - 1);
        previousSelfInput = (null == previousInputFrameDownsync ? 0 : previousInputFrameDownsync.InputList[joinIndex - 1]);
        if (
          null != existingInputFrame
          &&
          (true != canConfirmSelf)
        ) {
            return (previousSelfInput, existingInputFrame.InputList[joinIndex - 1]);
        }

        Array.Fill<ulong>(prefabbedInputList, 0);
        for (int k = 0; k < roomCapacity; ++k) {
            if (null != existingInputFrame) {
                // When "null != existingInputFrame", it implies that "true == canConfirmSelf" here, we just have to assign "prefabbedInputList[(joinIndex-1)]" specifically and copy all others
                prefabbedInputList[k] = existingInputFrame.InputList[k];
            }
            else if (lastIndividuallyConfirmedInputFrameId[k] <= inputFrameId) {
                prefabbedInputList[k] = lastIndividuallyConfirmedInputList[k];
                // Don't predict "btnA & btnB"!
                prefabbedInputList[k] = (prefabbedInputList[k] & 15);
            }
            else if (null != previousInputFrameDownsync) {
                // When "self.lastIndividuallyConfirmedInputFrameId[k] > inputFrameId", don't use it to predict a historical input!
                prefabbedInputList[k] = previousInputFrameDownsync.InputList[k];
                // Don't predict "btnA & btnB"!
                prefabbedInputList[k] = (prefabbedInputList[k] & 15);
            }
        }

        // [WARNING] Do not blindly use "selfJoinIndexMask" here, as the "actuallyUsedInput for self" couldn't be confirmed while prefabbing, otherwise we'd have confirmed a wrong self input by "_markConfirmationIfApplicable()"!
        ulong initConfirmedList = 0;
        if (null != existingInputFrame) {
            // When "null != existingInputFrame", it implies that "true == canConfirmSelf" here
            initConfirmedList = (existingInputFrame.ConfirmedList | selfJoinIndexMask);
        }
        // currSelfInput = ctrl.getEncodedInput(); // When "null == existingInputFrame", it'd be safe to say that the realtime "ctrl.getEncodedInput()" is for the requested "inputFrameId"
        prefabbedInputList[(joinIndex - 1)] = currSelfInput;
        while (inputBuffer.EdFrameId <= inputFrameId) {
            // Fill the gap
            int gapInputFrameId = inputBuffer.EdFrameId;
            inputBuffer.DryPut();
            var (ok, ifdHolder) = inputBuffer.GetByFrameId(gapInputFrameId);
            if (!ok || null == ifdHolder) {
                throw new ArgumentNullException(String.Format("inputBuffer was not fully pre-allocated for gapInputFrameId={0}! Now inputBuffer StFrameId={1}, EdFrameId={2}, Cnt/N={3}/{4}", gapInputFrameId, inputBuffer.StFrameId, inputBuffer.EdFrameId, inputBuffer.Cnt, inputBuffer.N));
            }

            ifdHolder.InputFrameId = gapInputFrameId;
            for (int k = 0; k < roomCapacity; ++k) {
                ifdHolder.InputList[k] = prefabbedInputList[k];
            }
            ifdHolder.ConfirmedList = initConfirmedList;
        }

        return (previousSelfInput, currSelfInput);
    }

    public (RoomDownsyncFrame, RoomDownsyncFrame) rollbackAndChase(int stRenderFrameId, int edRenderFrameId, CollisionSpace collisionSys, bool isChasing) {
        return (null, null);
    }

    public void applyRoomDownsyncFrameDynamics(RoomDownsyncFrame prevRdf, RoomDownsyncFrame rdf) {

    }

    public void _resetCurrentMatch() {
        renderFrameId = 0;
        chaserRenderFrameId = -1;
        maxChasingRenderFramesPerUpdate = 5;
        renderBufferSize = 256;
        playersArr = new GameObject[roomCapacity];
        lastIndividuallyConfirmedInputFrameId = new int[roomCapacity];
        Array.Fill<int>(lastIndividuallyConfirmedInputFrameId, -1);
        lastIndividuallyConfirmedInputList = new ulong[roomCapacity];
        Array.Fill<ulong>(lastIndividuallyConfirmedInputList, 0);
        renderBuffer = new FrameRingBuffer<RoomDownsyncFrame>(renderBufferSize);
        for (int i = 0; i < renderBufferSize; i++) {
            renderBuffer.Put(NewPreallocatedRoomDownsyncFrame(roomCapacity, 64, 64));
        }
        renderBuffer.Clear(); // Then use it by "DryPut"
        int inputBufferSize = (renderBufferSize >> 1) + 1;
        inputBuffer = new FrameRingBuffer<InputFrameDownsync>(inputBufferSize);
        for (int i = 0; i < inputBufferSize; i++) {
            inputBuffer.Put(NewPreallocatedInputFrameDownsync(roomCapacity));
        }
        inputBuffer.Clear(); // Then use it by "DryPut"
        selfPlayerInfo = new PlayerDownsync();
        selfPlayerInfo.JoinIndex = 1;
		var superMap = this.GetComponent<SuperTiled2Unity.SuperMap>();
        int mapWidth = superMap.m_Width, tileWidth = superMap.m_TileWidth, mapHeight = superMap.m_Height, tileHeight = superMap.m_TileHeight;
        spaceOffsetX = ((mapWidth * tileWidth) >> 1);
        spaceOffsetY = ((mapHeight * tileHeight) >> 1);

		collisionSys = new CollisionSpace(spaceOffsetX*2, spaceOffsetY*2, 64, 64);
        var grid = this.GetComponentInChildren<Grid>();
        foreach(Transform child in grid.transform) {
			if ("Barrier" == child.gameObject.name) {
				foreach(Transform barrierChild in child) {
					var barrierTileObj = barrierChild.gameObject.GetComponent<SuperTiled2Unity.SuperObject>();  
					var (tiledRectCx, tiledRectCy) = (barrierTileObj.m_X + barrierTileObj.m_Width*0.5, barrierTileObj.m_Y + barrierTileObj.m_Height*0.5);
					var (rectCx, rectCy) = tiledLayerOffsetToCollisionSpaceOffset(tiledRectCx, tiledRectCy, spaceOffsetX, spaceOffsetY);
					// [WARNING] The "Unity World (0, 0)" is aligned with the top-left corner of the rendered "TiledMap (via SuperMap)", to make it easy for me on debugging in collision space, I'm still using a "Collision Space (0, 0)" aligned with the center of the rendered "TiledMap (via SuperMap)" as the CocosCreator version. 
					var barrierCollider = GenerateRectCollider(rectCx, rectCy, barrierTileObj.m_Width, barrierTileObj.m_Height, 0, 0, 0, 0, 0, 0, null);	
					Debug.Log(String.Format("new barrierCollider=[X:{0}, Y:{1}, Width: {2}, Height: {3}]", barrierCollider.X, barrierCollider.Y, barrierCollider.W, barrierCollider.H));
					collisionSys.AddSingle(barrierCollider);
				}
			}
        }

        collisionHolder = new shared.Collision();
        // [WARNING] For "effPushbacks", "hardPushbackNormsArr" and "jumpedOrNotList", use array literal instead of "new Array" for compliance when passing into "gopkgs.ApplyInputFrameDownsyncDynamicsOnSingleRenderFrameJs"!
        effPushbacks = new Vector[roomCapacity];
        Array.Fill<Vector>(effPushbacks, new Vector(0, 0));
        hardPushbackNormsArr = new Vector[roomCapacity][];
        for (int i = 0; i < roomCapacity; i++) {
            hardPushbackNormsArr[i] = new Vector[5];
            Array.Fill<Vector>(hardPushbackNormsArr[i], new Vector(0, 0));
        }
        jumpedOrNotList = new bool[roomCapacity];
        Array.Fill(jumpedOrNotList, false);
        dynamicRectangleColliders = new shared.Collider[64];
        Array.Fill(dynamicRectangleColliders, GenerateRectCollider(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, null));
        prefabbedInputListHolder = new ulong[roomCapacity];
    }

	void OnRenderObject() {
		CreateLineMaterial();
        lineMaterial.SetPass(0);

        GL.PushMatrix();
        // Set transformation matrix for drawing to
        // match our transform
        GL.MultMatrix(transform.localToWorldMatrix);
        var grid = this.GetComponentInChildren<Grid>();
        foreach(Transform child in grid.transform) {
			if ("Barrier" == child.gameObject.name) {
				foreach(Transform barrierChild in child) {
					var barrierTileObj = barrierChild.gameObject.GetComponent<SuperTiled2Unity.SuperObject>();  
					var (tiledRectCx, tiledRectCy) = (barrierTileObj.m_X + barrierTileObj.m_Width*0.5, barrierTileObj.m_Y + barrierTileObj.m_Height*0.5);
					var (rectCx, rectCy) = tiledLayerOffsetToCollisionSpaceOffset(tiledRectCx, tiledRectCy, spaceOffsetX, spaceOffsetY);
					var barrierCollider = GenerateRectCollider(rectCx, rectCy, barrierTileObj.m_Width, barrierTileObj.m_Height, 0, 0, 0, 0, 0, 0, null);	
					
					GL.Begin(GL.LINES);
					for (int i = 0; i < 4; i++) {
						switch (i) {
							case 0:
								GL.Vertex3((float)(barrierCollider.X+spaceOffsetX), (float)(barrierCollider.Y-spaceOffsetY), 0);
								GL.Vertex3((float)(barrierCollider.X+barrierCollider.W+spaceOffsetX), (float)(barrierCollider.Y-spaceOffsetY), 0);
								break;
							case 1:
								GL.Vertex3((float)(barrierCollider.X+barrierCollider.W+spaceOffsetX), (float)(barrierCollider.Y-spaceOffsetY), 0);
								GL.Vertex3((float)(barrierCollider.X+barrierCollider.W+spaceOffsetX), (float)(barrierCollider.Y+barrierCollider.H-spaceOffsetY), 0);
								break;
							case 2:
								GL.Vertex3((float)(barrierCollider.X+barrierCollider.W+spaceOffsetX), (float)(barrierCollider.Y+barrierCollider.H-spaceOffsetY), 0);
								GL.Vertex3((float)(barrierCollider.X+spaceOffsetX), (float)(barrierCollider.Y+barrierCollider.H-spaceOffsetY), 0);
								break;
							case 3:
								GL.Vertex3((float)(barrierCollider.X+spaceOffsetX), (float)(barrierCollider.Y+barrierCollider.H-spaceOffsetY), 0);
								GL.Vertex3((float)(barrierCollider.X+spaceOffsetX), (float)(barrierCollider.Y-spaceOffsetY), 0);
								break;
						}
					}
					GL.End();
				}
			}
        }
        GL.PopMatrix();
    }

	static Material lineMaterial;
    static void CreateLineMaterial() {
        if (!lineMaterial) {
            // Unity has a built-in shader that is useful for drawing
            // simple colored things.
            Shader shader = Shader.Find("Hidden/Internal-Colored");
            lineMaterial = new Material(shader);
            lineMaterial.hideFlags = HideFlags.HideAndDontSave;
            // Turn on alpha blending
            lineMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            lineMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            // Turn backface culling off
            lineMaterial.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
            // Turn off depth writes
            lineMaterial.SetInt("_ZWrite", 0);
        }
    }
}

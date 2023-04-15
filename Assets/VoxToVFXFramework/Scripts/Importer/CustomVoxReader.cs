using System;
using System.Collections;
using FileToVoxCore.Vox;
using System.IO;
using Unity.Collections;
using UnityEngine;

namespace VoxToVFXFramework.Scripts.Importer
{
	public class CustomVoxReader : VoxReader
	{
		public void LoadModelAsync(string absolutePath, Action<float> progressCallback, Action<VoxModelCustom> resultBack)
		{
			var name = Path.GetFileNameWithoutExtension(absolutePath);
			VoxelCountLastXyziChunk = 0;
			LogOutputFile = name + "-" + DateTime.Now.ToString("y-MM-d_HH.m.s") + ".txt";
			ChildCount = 0;
			ChunkCount = 0;
			using (BinaryReader reader = new BinaryReader(new MemoryStream(File.ReadAllBytes(absolutePath))))
			{
				var head = new string(reader.ReadChars(4));
				if (!head.Equals(HEADER))
				{
					Console.WriteLine("Not a Magicavoxel file! ");
					resultBack?.Invoke(null);
					return;
				}

				int version = reader.ReadInt32();
				if (version != VERSION)
				{
					Console.WriteLine("Version number: " + version + " Was designed for version: " + VERSION);
				}
				while (reader.BaseStream.Position != reader.BaseStream.Length)
				{
					VoxelDataCreatorManager.Instance.StartCoroutine(ReadChunkAsync(reader, progressCallback, ((output) => OnAllChunksReaded(output, resultBack))));
				}
			}
		}

		private void OnAllChunksReaded(VoxModelCustom output, Action<VoxModelCustom> resultBack)
		{
			output.Palette ??= LoadDefaultPalette();
			resultBack?.Invoke(output);
		}

		protected IEnumerator ReadChunkAsync(BinaryReader reader, Action<float> progressCallback, Action<VoxModelCustom> resultCallback)
		{
			VoxModelCustom output = new VoxModelCustom();
			var chunkName = new string(reader.ReadChars(4));
			var chunkSize = reader.ReadInt32();
			var childChunkSize = reader.ReadInt32();
			var chunk = reader.ReadBytes(chunkSize);
			var children = reader.ReadBytes(childChunkSize);
			ChunkCount++;

			using (var chunkReader = new BinaryReader(new MemoryStream(chunk)))
			{
				switch (chunkName)
				{
					case MAIN:
						break;
					case SIZE:
						ReadSIZENodeChunk(chunkReader, output);
						break;
					case XYZI:
						ReadXYZINodeChunk(chunkReader, output);
						break;
					case RGBA:
						output.Palette = LoadPalette(chunkReader);
						break;
					case MATT:
						break;
					case PACK:
						int frameCount = chunkReader.ReadInt32();
						for (int i = 0; i < frameCount; i++)
						{
							output.VoxelFrames.Add(new VoxelData());
						}
						break;
					case nTRN:
						output.TransformNodeChunks.Add(ReadTransformNodeChunk(chunkReader));
						break;
					case nGRP:
						output.GroupNodeChunks.Add(ReadGroupNodeChunk(chunkReader));
						break;
					case nSHP:
						output.ShapeNodeChunks.Add(ReadShapeNodeChunk(chunkReader));
						break;
					case LAYR:
						output.LayerChunks.Add(ReadLayerChunk(chunkReader));
						break;
					case MATL:
						output.MaterialChunks.Add(ReadMaterialChunk(chunkReader));
						break;
					case rOBJ:
						output.RendererSettingChunks.Add(ReaddRObjectChunk(chunkReader));
						break;
					case IMAP:
						output.PaletteColorIndex = new int[256];
						for (int i = 0; i < 256; i++)
						{
							int index = chunkReader.ReadByte();
							output.PaletteColorIndex[i] = index;
						}
						break;
					default:
						Console.WriteLine($"Unknown chunk: \"{chunkName}\"");
						break;
				}
			}

			//read child chunks
			using (var childReader = new BinaryReader(new MemoryStream(children)))
			{
				while (childReader.BaseStream.Position != childReader.BaseStream.Length)
				{
					Debug.Log(childReader.BaseStream.Position / (float)childReader.BaseStream.Length);
					progressCallback?.Invoke(childReader.BaseStream.Position / (float)childReader.BaseStream.Length);
					yield return new WaitForEndOfFrame();
					ReadChunk(childReader, output);
				}
			}

			resultCallback?.Invoke(output);
		}

		protected override void ReadSIZENodeChunk(BinaryReader chunkReader, VoxModel output)
		{
			VoxModelCustom outputCasted = output as VoxModelCustom;

			int xSize = chunkReader.ReadInt32();
			int ySize = chunkReader.ReadInt32();
			int zSize = chunkReader.ReadInt32();
			if (ChildCount >= outputCasted.VoxelFramesCustom.Count)
				outputCasted.VoxelFramesCustom.Add(new VoxelDataCustom());

			//Swap XZ
			//Dirty but resize add +1 
			outputCasted.VoxelFramesCustom[ChildCount].Resize(xSize - 1, zSize - 1, ySize - 1);
			ChildCount++;
		}

		protected override void ReadXYZINodeChunk(BinaryReader chunkReader, VoxModel output)
		{
			VoxModelCustom outputCasted = output as VoxModelCustom;
			int voxelCountLastXyziChunk = chunkReader.ReadInt32();
			VoxelDataCustom frame = outputCasted.VoxelFramesCustom[ChildCount - 1];
			frame.VoxelNativeArray = new NativeArray<byte>((frame.VoxelsWide) * (frame.VoxelsTall) * (frame.VoxelsDeep), Allocator.Persistent);
			for (int i = 0; i < voxelCountLastXyziChunk; i++)
			{
				int x = frame.VoxelsWide - 1 - chunkReader.ReadByte(); //invert
				int z = frame.VoxelsDeep - 1 - chunkReader.ReadByte(); //swapYZ //invert
				int y = chunkReader.ReadByte();
				byte color = chunkReader.ReadByte();
				if (color > 0)
				{
					frame.VoxelNativeArray[frame.GetGridPos(x, y, z)] = color;
				}
			}
		}
	}
}

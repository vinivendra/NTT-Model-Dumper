using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Toolbox.Core.IO;
using OpenTK;
using IONET.Core.Model;
using IONET.Core;
using System.IO;

namespace TTLibrary
{
    internal class Model
    {
        public string Name { get; set; }

        public Model(string filePath) {
            Read(new FileReader(filePath));
        }

        private void Read(FileReader reader)
        {
            //Read as big endian. Only the raw vertex/index data is little endian
            reader.SetByteOrder(true);
            //Read to the end and make sure all file chunks are loaded
            while (reader.BaseStream.Length > reader.Position) {
                long pos = reader.Position;
                var chunk = reader.ReadStruct<ResourceChunk>();
                Console.WriteLine("=============");
                Console.WriteLine($"Read chunk: size {chunk.Size}, type: {new string(chunk.Type)}, version: {chunk.Version}" );
                ParseChunk(reader, chunk);
                reader.SeekBegin(pos + chunk.Size + 4);
            }
        }

        private void ParseChunk(FileReader reader, ResourceChunk chunk)
        {
            string type = new string(chunk.Type);
            switch (type)
            {
                case ".CC4HSERHSER": //Resource Hierarchy
                    Console.WriteLine("Reading resource hierarchy (CC4HSERHSER)");
                    string bla = reader.ReadZeroTerminatedString();
                    Console.WriteLine($"-- \"{bla}\"");
                    Name = ReadString(reader);
                    Console.WriteLine($"Name: \"{Name}\"");
                    break;
                case ".CC4HSER2CSG": //Scene Data
                    Console.WriteLine("Reading scene data (CC4HSER2CSG)");
                    ParseCSG(reader);
                    break;
            }
        }

        private void ParseCSG(FileReader reader)
        {
            IOModel model = new IOModel();
            model.Name = "Model";

            uint numMaterials = reader.ReadUInt32();
            Console.WriteLine($"numMaterials: \"{numMaterials}\"");
            ushort unk = reader.ReadUInt16();
            Console.WriteLine($"unk: \"{unk}\"");
            reader.ReadInt16(); //padding

            //Read materials first
            Console.WriteLine($"Reading materials...");
            Material[] materials = new Material[numMaterials];
            for (int i = 0; i < numMaterials; i++)
            {
                Console.WriteLine($"\t---");
                Console.WriteLine($"\tStart at {reader.Position.ToString("X")}");
                materials[i] = new Material();
                reader.ReadChar(); // Padding
                materials[i].FilePath = ReadString(reader);

                Console.WriteLine($"\tPadding 1 [1] at {reader.Position.ToString("X")}");
                reader.ReadChar(); // Padding
                materials[i].Name = ReadString(reader);

                Console.WriteLine($"\tPadding 2 [3] at {reader.Position.ToString("X")}");
                string padding2 = new string(reader.ReadChars(3));
                Console.WriteLine($"\tMaterial {materials[i].Name} at {materials[i].FilePath}");
            }

            Console.WriteLine($"Reading sub meshes...");
            Console.WriteLine($"\tStart at {reader.Position.ToString("X")}");
            Console.WriteLine($"\tPadding 1 [UInt32] at {reader.Position.ToString("X")}");
            //Next is the sub meshes.
            reader.ReadUInt32(); //padding
            uint numMeshes = reader.ReadUInt32();
            Console.WriteLine($"\tnumMeshes: {numMeshes}");
            Console.WriteLine($"\tPadding 1 [UInt32] at {reader.Position.ToString("X")}");
            reader.ReadUInt32(); //1

            SubMesh[] meshes = new SubMesh[numMeshes];
            for (int i = 0; i < numMeshes; i++)
            {
                // Make sure we're starting this mesh at the right place
                SeekToSMNR(reader, 128, 0);

				Console.WriteLine($"Reading mesh {i+1}/{numMeshes}...");
                Console.WriteLine($"\tRead submesh at {reader.Position.ToString("X")}");
                meshes[i] = reader.ReadStruct<SubMesh>();
                Console.WriteLine($"\tSubmesh:");
                Console.WriteLine($"\t\tmagic {meshes[i].Magic}");
                Console.WriteLine($"\t\tNumBuffers {meshes[i].NumBuffers}");
                Console.WriteLine($"\t\tNumVerts {meshes[i].NumVerts}");
                Console.WriteLine($"\t\tVersion {meshes[i].Version}");
                Console.WriteLine($"\t\tUnknown1 {meshes[i].Unknown1}");
                Console.WriteLine($"\t\tUnknown2 {meshes[i].Unknown2}");

                Console.WriteLine($"\t\tParse DXTV at {reader.Position.ToString("X")}");
                Buffer[] buffers = ParseDXTV(reader, meshes[i]);

                //Indices come after all the buffer DXTVs
                uint numIndices = reader.ReadUInt32();
                Console.WriteLine($"\tnumIndices {numIndices}");
                uint indexFormat = reader.ReadUInt32(); //2 for ushort
                Console.WriteLine($"\tindexFormat {indexFormat}");

                if (indexFormat != 2 && indexFormat != 4)
                    throw new Exception($"Unknown index format! {indexFormat}");

                reader.SetByteOrder(false);
                Console.WriteLine($"\tRead indices at {reader.Position.ToString("X")}");

                uint[] indices = new uint[numIndices];
                for (int j = 0; j < numIndices; j++)
                {
                    if (indexFormat == 2) indices[j] = reader.ReadUInt16();
                    if (indexFormat == 4) indices[j] = reader.ReadUInt32();
                }

                reader.SetByteOrder(true);
                //70 extra bytes we need to skip till the next sub mesh section
                Console.WriteLine($"\tPadding {70} bytes at {reader.Position.ToString("X")}");
                reader.Seek(70);

				if (i < numMeshes + 1)
                {
					// Try to find the "SMNR" string that starts the next submesh, looking through the next 128 bytes.
					// If we don't find it, default to skipping the next (4 * buffers.Length) bytes.
					SeekToSMNR(reader, 128, 4 * buffers.Length);
				}

				//Convert the buffers and indices to a usable .iomesh to make a .dae file
				Console.WriteLine($"\tConverting to DAE");
				model.Meshes.Add(ConvertToDae(meshes[i], i, buffers, indices));
			}
            //Convert to ioscene and export as .dae
            var scene = new IOScene();
            scene.Models.Add(model);

            Console.WriteLine($"=============");
            // Name is "-Assets/Core/RopeAssets/ledge_end2_dx11.model"
            // There's a random character before the folder name that needs to be removed
            Console.WriteLine($"{Name}");
            var relativePath = Name.Remove(0,1);
            // relativePath == "Assets/Core/RopeAssets/ledge_end2_dx11.model"
            relativePath = relativePath.Replace("/","\\");
            // relativePath == "Assets\Core\RopeAssets\ledge_end2_dx11.model"
            var extractedFolder = "C:\\Users\\Vini\\Desktop\\TSS\\NTT-Model-Dumper-main\\bin\\Debug\\net5.0\\Extracted";
            var newPath = System.IO.Path.Combine(extractedFolder, relativePath);
            // newPath == "G:\Extracted\Assets\Core\RopeAssets\ledge_end2_dx11.model"
            Console.WriteLine($"{newPath}");
            var outputPath = System.IO.Path.ChangeExtension(newPath, "dae");
            // outputPath == "G:\Extracted\Assets\Core\RopeAssets\ledge_end2_dx11.dae"
            Console.WriteLine($"{outputPath}");
            var outputFolder = System.IO.Path.GetDirectoryName(outputPath);
            // outputPath == "G:\Extracted\Assets\Core\RopeAssets"
            Console.WriteLine($"{outputFolder}");

            // Create the folder (fails silently if it already exists)
            System.IO.Directory.CreateDirectory(outputFolder);

            // Export the file
            IONET.IOManager.ExportScene(scene, outputPath);

            Console.WriteLine();
        }

        public void SeekToSMNR(FileReader reader, int maximumOffest, int fallbackOffset)
        {
			Console.WriteLine($"\tLooking for SMNR from {reader.Position.ToString("X")} through the next {maximumOffest} bytes");

			// The next submesh section starts with an SMNR string, which has some unknown padding before it
			int searchIndex = 1;
			int k = 0;
			String SMNR = "SMNR";
			for (searchIndex = 1; searchIndex <= maximumOffest; searchIndex++)
			{
                try
                {
                    int character = reader.ReadChar();

					if (character == SMNR[k])
					{
						k++;
						// If we finished reading the SMNR string
						if (k == 4)
						{
							// Reset the reader's position to just before the SMNR string
							reader.Seek(-4);
							Console.WriteLine($"\tNext SMNR starts at {reader.Position.ToString("X")}");
							break;
						}
					}
				}
                catch (EndOfStreamException)
                {
                    // Leave the for loop and use the fallback offset instead
                    k = 0;
                    break;
                }
			}
			if (k != 4)
			{
				// If we didn't manually find the SMNR string, try rewinding the SMNR search (by j bytes) and instead
				// advancing to the fallback offset
				int seekOffset = -searchIndex + fallbackOffset;
				Console.WriteLine($"\tDidn't find SMNR, seeking by {seekOffset} bytes from {reader.Position.ToString("X")}");
				reader.Seek(seekOffset);
				Console.WriteLine($"\tSeek complete, starting at {reader.Position.ToString("X")}");
			}
		}

		public string ReadSignature(FileReader reader, int length, string ExpectedSignature, bool TrimEnd = false)
		{
			string text = reader.ReadString(length, Encoding.ASCII);
            Console.WriteLine(text);
			if (TrimEnd)
			{
				Console.WriteLine("Trimming");
				text = text.TrimEnd(' ');
				Console.WriteLine("Trimmed");
			}

			if (text != ExpectedSignature)
			{
                Console.WriteLine("Throwing");
				throw new Exception("Invalid signature " + text + "! Expected " + ExpectedSignature + ".");
			}

			Console.WriteLine("Returning");
			return text;
		}

		private Buffer[] ParseDXTV(FileReader reader, SubMesh mesh)
        {
            Console.WriteLine($"\tStarting DXTV at {reader.Position.ToString("X")}");
            Buffer[] buffers = new Buffer[mesh.NumBuffers];
            for (int i = 0; i < mesh.NumBuffers; i++)
            {
                Buffer buffer = new Buffer();
                buffers[i] = buffer;

                Console.WriteLine($"\tBuffer {i+1}/{mesh.NumBuffers} at {reader.Position.ToString("X")}");

				ReadSignature(reader, 4, "DXTV");
				uint version = reader.ReadUInt32();
				uint numAttributes = reader.ReadUInt32();
				Console.WriteLine($"\t\tVersion {version}");
                Console.WriteLine($"\t\tnumAttributes: {numAttributes}");

                buffer.Attributes = new Attribute[numAttributes];
                for (int j = 0; j < numAttributes; j++) {
                    Console.WriteLine($"\t\tAttribute {j+1}/{numAttributes}");
                    buffer.Attributes[j] = new Attribute()
                    {
                        Type = (AttributeType)reader.ReadByte(),
                        Format = (AttributeFormat)reader.ReadByte(),
                        Offset = reader.ReadByte(),
                    };
                    Console.WriteLine($"\t\t\ttype: {buffer.Attributes[j].Type}");
                    Console.WriteLine($"\t\t\tformat: {buffer.Attributes[j].Format}");
                    Console.WriteLine($"\t\t\toffset: {buffer.Attributes[j].Offset}");
                }

                Console.WriteLine($"\t\tPadding 6 at {reader.Position.ToString("X")}");
                reader.Seek(6);
                Console.WriteLine($"\t\tParse vertices starting at {reader.Position.ToString("X")}");
                var stride = buffer.Attributes.Sum(x => x.GetStride());

                for (int j = 0; j < numAttributes; j++)
                    buffer.Attributes[j].Data = new Vector4[mesh.NumVerts];

                reader.SetByteOrder(false);

                long pos = reader.Position;
                for (int v = 0; v < mesh.NumVerts; v++) {
                    for (int j = 0; j < numAttributes; j++) {
                        reader.SeekBegin(pos + stride * v + buffer.Attributes[j].Offset);
                        buffer.Attributes[j].Data[v] = ParseVertex(reader, buffer.Attributes[j].Format);
                    }
                }
                reader.SetByteOrder(true);

                Console.WriteLine($"\t\tSeek begin for {pos + stride * mesh.NumVerts + 16} bytes at {reader.Position.ToString("X")}");
                reader.SeekBegin(pos + stride * mesh.NumVerts + 16);
            }
            Console.WriteLine($"\tFinished DXTV at {reader.Position.ToString("X")}");
            return buffers;
        }

        public Vector4 ParseVertex(FileReader reader, AttributeFormat format)
        {
            switch (format)
            {
                case AttributeFormat.Vec2Float:
                    return new Vector4(
                        reader.ReadSingle(), reader.ReadSingle(), 0, 0);
                case AttributeFormat.Vec3Float:
                    return new Vector4(
                        reader.ReadSingle(), reader.ReadSingle(),
                        reader.ReadSingle(), 0);
                case AttributeFormat.Vec4Float:
                    return new Vector4(
                        reader.ReadSingle(), reader.ReadSingle(),
                        reader.ReadSingle(), reader.ReadSingle());
                case AttributeFormat.Vec4Byte:
                    return new Vector4(
                        reader.ReadByte(), reader.ReadByte(),
                        reader.ReadByte(), reader.ReadByte());
                case AttributeFormat.Vec4ByteF:
                    return new Vector4(
                        reader.ReadByte() / 255.0f, reader.ReadByte() / 255.0f,
                        reader.ReadByte() / 255.0f, reader.ReadByte() / 255.0f);
                case AttributeFormat.Vec2Half:
                    return new Vector4(
                        reader.ReadHalfSingle(), reader.ReadHalfSingle(),  0, 0);
                case AttributeFormat.Vec4Half:
                    return new Vector4(
                        reader.ReadHalfSingle(), reader.ReadHalfSingle(),
                        reader.ReadHalfSingle(), reader.ReadHalfSingle());
                case AttributeFormat.Color4Byte:
                    return new Vector4(
                        reader.ReadByte(), reader.ReadByte(),
                        reader.ReadByte(), reader.ReadByte());
            }
            return Vector4.Zero;
        }

        private string ReadString(FileReader reader)
        {
            string str = reader.ReadZeroTerminatedString();
            reader.ReadZeroTerminatedString();
            return str;
        }
        private IOMesh ConvertToDae(SubMesh subMesh, int index, Buffer[] buffers, uint[] indices)
        {
            //Convertable IO mesh to .dae
            IOMesh mesh = new IOMesh();
            mesh.Name = $"Mesh_{index}";

            for (int v = 0; v < subMesh.NumVerts; v++)
            {
                IOVertex vertex = new IOVertex();
                mesh.Vertices.Add(vertex);

                foreach (var buffer in buffers)
                {
                    foreach (var attribute in buffer.Attributes)
                    {
                        switch (attribute.Type)
                        {
                            case AttributeType.Position:
                                vertex.Position = new System.Numerics.Vector3(
                                     attribute.Data[v].X, attribute.Data[v].Y, attribute.Data[v].Z);
                                break;
                            case AttributeType.Normal:
                                vertex.Normal = new System.Numerics.Vector3(
                                     attribute.Data[v].X, attribute.Data[v].Y, attribute.Data[v].Z);
                                break;
                            case AttributeType.UvSet01:
                                vertex.SetUV(attribute.Data[v].X, attribute.Data[v].Y, 0);
                                break;
                            case AttributeType.UvSet2:
                                vertex.SetUV(attribute.Data[v].X, attribute.Data[v].Y, 1);
                                break;
                            case AttributeType.ColorSet0:
                                vertex.SetColor(attribute.Data[v].X/255, attribute.Data[v].Y/255, attribute.Data[v].Z/255, attribute.Data[v].W/255, 0);
                                break;
                            case AttributeType.ColorSet1:
                                vertex.SetColor(attribute.Data[v].X/255, attribute.Data[v].Y/255, attribute.Data[v].Z/255, attribute.Data[v].W/255, 1);
                                break;
                        }
                    }
                }
            }

            var poly = new IOPolygon();
            for (int j = 0; j < indices.Length; j++)
                poly.Indicies.Add((int)indices[j]);

            mesh.Polygons.Add(poly);
            return mesh;
        }

        public class Buffer
        {
            public Attribute[] Attributes { get; set; }
            public Vector4[] Data { get; set; }
        }

        public class Material
        {
            public string FilePath { get; set; }
            public string Name { get; set; }
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public class SubMesh
        {
            public uint Magic;
            public uint Version;
            public uint NumBuffers;
            public uint Unknown1;
            public uint Unknown2;
            public uint NumVerts;
        }

        public class Attribute
        {
            public AttributeType Type;
            public AttributeFormat Format;
            public byte Offset;

            public uint GetStride()
            {
                switch (Format)
                {
                    case AttributeFormat.Vec2Float: return 8;
                    case AttributeFormat.Vec3Float: return 12;
                    case AttributeFormat.Vec4Float: return 16;
                    case AttributeFormat.Vec2Half: return 4;
                    case AttributeFormat.Vec4Half: return 8;
                    case AttributeFormat.Vec4Byte: return 4;
                    case AttributeFormat.Vec4ByteF: return 4;
                    case AttributeFormat.Color4Byte: return 4;
                    default:
                        throw new Exception($"Unknown format! {Format}");
                }
            }
            public Vector4[] Data { get; set; }
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public class ResourceChunk
        {
            public uint Size;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 12)]
            public char[] Type;
            public uint Version;
        }

        public enum AttributeType : byte
        {
            Position = 0,
            Normal = 1,
            ColorSet0 = 2,
            Tangent = 3,
            ColorSet1 = 4,
            UvSet01 = 5,
            unknown6 = 6,
            UvSet2 = 7,
            Unknown8 = 8,
            BlendIndices0 = 9,
            BlendWeight0 = 10,
            Unknown11 = 11, 
            LightDirSet = 12, 
            LightColSet = 13, 
        }

        public enum AttributeFormat : byte
        {
            Vec2Float = 2,
            Vec3Float = 3,
            Vec4Float = 4,
            Vec2Half = 5,
            Vec4Half = 6,
            Vec4Byte = 7,
            Vec4ByteF = 8,
            Color4Byte = 9,
        }
    }
}

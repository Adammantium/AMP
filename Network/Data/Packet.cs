﻿using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace AMP.Network.Data {
    public class Packet : IDisposable {
		private List<byte> buffer = new List<byte>();

		private byte[] readableBuffer;
		private int readPos;
		private bool disposed;

		public enum Type {
			welcome		= 1,
			disconnect	= 2,
			error		= 3,
			message		= 4,
			
			playerData	= 5,
			playerPos	= 6,

			itemSpawn	= 7,
			itemDespawn = 8,
			itemPos 	= 9,
		}

		public Packet() {
			readPos = 0;
		}

		public Packet(int id) {
			readPos = 0;
			Write(id);
		}

		public Packet(byte[] data) {
			readPos = 0;
			SetBytes(data);
		}

		public void SetBytes(byte[] data) {
			Write(data);
			readableBuffer = buffer.ToArray();
		}

		public void InsertInt(int value) {
			buffer.InsertRange(0, BitConverter.GetBytes(value));
		}

		public byte[] ToArray() {
			readableBuffer = buffer.ToArray();
			return readableBuffer;
		}

		public int Length() {
			return buffer.Count;
		}

		public int UnreadLength() {
			return Length() - readPos;
		}

		public void Reset(bool shouldReset = true) {
			if(shouldReset) {
				buffer.Clear();
				readableBuffer = null;
				readPos = 0;
				return;
			}
			readPos -= 4;
		}

		#region writing
		public void WriteLength() {
			buffer.InsertRange(0, BitConverter.GetBytes(buffer.Count));
		}

		public void Write(byte value) {
			buffer.Add(value);
		}

		public void Write(byte[] value) {
			buffer.AddRange(value);
		}

		public void Write(short value) {
			buffer.AddRange(BitConverter.GetBytes(value));
		}

		public void Write(int value) {
			buffer.AddRange(BitConverter.GetBytes(value));
		}

		public void Write(long value) {
			buffer.AddRange(BitConverter.GetBytes(value));
		}

		public void Write(float value) {
			buffer.AddRange(BitConverter.GetBytes(value));
		}

		public void Write(bool value) {
			buffer.AddRange(BitConverter.GetBytes(value));
		}

		public void Write(string value) {
			Write(value.Length);
			buffer.AddRange(Encoding.ASCII.GetBytes(value));
		}

		public void Write(Vector3 value) {
			Write(value.x);
			Write(value.y);
			Write(value.z);
		}

		public void Write(Quaternion value) {
			Write(value.x);
			Write(value.y);
			Write(value.z);
			Write(value.w);
		}

		public void Write(Color value) {
			Write(value.r);
			Write(value.g);
			Write(value.b);
		}

		public void Write(Vector3 value, Vector3 oldValue) {
			if(value == oldValue) Write(false);
			else { Write(true); Write(value); }
		}

		public void Write(Quaternion value, Quaternion oldValue) {
			if(value == oldValue) Write(false);
			else { Write(true); Write(value); }
		}
		#endregion

		#region reading
		public byte ReadByte(bool moveReadPos = true) {
			if(buffer.Count > readPos) {
				byte result = readableBuffer[readPos];
				if(moveReadPos) {
					readPos++;
				}
				return result;
			}
			throw new Exception("Could not read value of type 'byte'!");
		}

		public byte[] ReadBytes(int length, bool moveReadPos = true) {
			if(buffer.Count > readPos) {
				byte[] result = buffer.GetRange(readPos, length).ToArray();
				if(moveReadPos) {
					readPos += length;
				}
				return result;
			}
			throw new Exception("Could not read value of type 'byte[]'!");
		}

		public short ReadShort(bool moveReadPos = true) {
			if(buffer.Count > readPos) {
				short result = BitConverter.ToInt16(readableBuffer, readPos);
				if(moveReadPos) {
					readPos += 2;
				}
				return result;
			}
			throw new Exception("Could not read value of type 'short'!");
		}

		public int ReadInt(bool moveReadPos = true) {
			if(buffer.Count > readPos) {
				int result = BitConverter.ToInt32(readableBuffer, readPos);
				if(moveReadPos) {
					readPos += 4;
				}
				return result;
			}
			throw new Exception("Could not read value of type 'int'!");
		}

		public long ReadLong(bool moveReadPos = true) {
			if(buffer.Count > readPos) {
				long result = BitConverter.ToInt64(readableBuffer, readPos);
				if(moveReadPos) {
					readPos += 8;
				}
				return result;
			}
			throw new Exception("Could not read value of type 'long'!");
		}

		public float ReadFloat(bool moveReadPos = true) {
			if(buffer.Count > readPos) {
				float result = BitConverter.ToSingle(readableBuffer, readPos);
				if(moveReadPos) {
					readPos += 4;
				}
				return result;
			}
			throw new Exception("Could not read value of type 'float'!");
		}

		public bool ReadBool(bool moveReadPos = true) {
			if(buffer.Count > readPos) {
				bool result = BitConverter.ToBoolean(readableBuffer, readPos);
				if(moveReadPos) {
					readPos++;
				}
				return result;
			}
			throw new Exception("Could not read value of type 'bool'!");
		}

		public string ReadString(bool moveReadPos = true) {
			string result;
			try {
				int num = ReadInt(true);
				string @string = Encoding.ASCII.GetString(readableBuffer, readPos, num);
				if(moveReadPos && @string.Length > 0) {
					readPos += num;
				}
				result = @string;
			} catch {
				throw new Exception("Could not read value of type 'string'!");
			}
			return result;
		}

		public Vector3 ReadVector3(bool moveReadPos = true) {
			return new Vector3(ReadFloat(moveReadPos), ReadFloat(moveReadPos), ReadFloat(moveReadPos));
		}

		public Vector3 ReadVector3Optimised(bool moveReadPos = true) {
			// If should change return new value
			if(ReadBool()) return new Vector3(ReadFloat(moveReadPos), ReadFloat(moveReadPos), ReadFloat(moveReadPos));
			// If shouldn't change return 0
			else return Vector3.zero;
		}

		public Quaternion ReadQuaternion(bool moveReadPos = true) {
			return new Quaternion(ReadFloat(moveReadPos), ReadFloat(moveReadPos), ReadFloat(moveReadPos), ReadFloat(moveReadPos));
		}

		public Quaternion ReadQuaternionOptimised(bool moveReadPos = true) {
			// If should change return new value
			if(ReadBool()) return new Quaternion(ReadFloat(moveReadPos), ReadFloat(moveReadPos), ReadFloat(moveReadPos), ReadFloat(moveReadPos));
			// If shouldn't change return 0
			else return Quaternion.identity;
		}

		public Color ReadColor(bool moveReadPos = true) {
			return new Color(ReadFloat(moveReadPos), ReadFloat(moveReadPos), ReadFloat(moveReadPos));
		}
		#endregion

		protected virtual void Dispose(bool disposing) {
			if(!disposed) {
				if(disposing) {
					buffer = null;
					readableBuffer = null;
					readPos = 0;
				}
				disposed = true;
			}
		}

		public void Dispose() {
			Dispose(true);
			GC.SuppressFinalize(this);
		}
	}
}
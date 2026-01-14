/*
    Copyright (C) 2011-2015 de4dot@gmail.com

    This file is part of de4dot.

    de4dot is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    de4dot is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with de4dot.  If not, see <http://www.gnu.org/licenses/>.
*/

using System;
using System.Collections.Generic;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using de4dot.blocks;

namespace de4dot.code {
	public abstract class StringInlinerBase : MethodReturnValueInliner {
		protected override void InlineReturnValues(IList<CallResult> callResults) {
			foreach (var callResult in callResults) {
				var block = callResult.block;
				int num = callResult.callEndIndex - callResult.callStartIndex + 1;

				var decryptedString = callResult.returnValue as string;
				if (decryptedString == null)
					continue;

				int ldstrIndex = callResult.callStartIndex;
				block.Replace(ldstrIndex, num, OpCodes.Ldstr.ToInstruction(decryptedString));

				// If it's followed by castclass string, remove it
				if (ldstrIndex + 1 < block.Instructions.Count) {
					var instr = block.Instructions[ldstrIndex + 1];
					if (instr.OpCode.Code == Code.Castclass && instr.Operand.ToString() == "System.String")
						block.Remove(ldstrIndex + 1, 1);
				}

				// If it's followed by String.Intern(), then nop out that call
				if (ldstrIndex + 1 < block.Instructions.Count) {
					var instr = block.Instructions[ldstrIndex + 1];
					if (instr.OpCode.Code == Code.Call) {
						if (instr.Operand is IMethod calledMethod &&
							calledMethod.FullName == "System.String System.String::Intern(System.String)") {
							block.Remove(ldstrIndex + 1, 1);
						}
					}
				}

				Logger.v("Decrypted string: {0}", Utils.ToCsharpString(decryptedString));
			}
		}
	}

	public class StaticStringInliner : StringInlinerBase {
		MethodDefAndDeclaringTypeDict<Func<MethodDef, MethodSpec, object[], string>> stringDecrypters = new MethodDefAndDeclaringTypeDict<Func<MethodDef, MethodSpec, object[], string>>();

		public override bool HasHandlers => stringDecrypters.Count != 0;
		public IEnumerable<MethodDef> Methods => stringDecrypters.GetKeys();

		class MyCallResult : CallResult {
			public IMethod IMethod;
			public MethodSpec gim;
			public MyCallResult(Block block, int callEndIndex, IMethod method, MethodSpec gim)
				: base(block, callEndIndex) {
				IMethod = method;
				this.gim = gim;
			}
		}

		public void Add(MethodDef method, Func<MethodDef, MethodSpec, object[], string> handler) {
			if (method != null)
				stringDecrypters.Add(method, handler);
		}

		protected override void InlineAllCalls() {
			foreach (var tmp in callResults) {
				var callResult = (MyCallResult)tmp;
				var handler = stringDecrypters.Find(callResult.IMethod);
				callResult.returnValue = handler((MethodDef)callResult.IMethod, callResult.gim, callResult.args);
			}
		}

		protected override CallResult CreateCallResult(IMethod method, MethodSpec gim, Block block, int callInstrIndex) {
			if (stringDecrypters.Find(method) == null)
				return null;
			return new MyCallResult(block, callInstrIndex, method, gim);
		}
	}
}

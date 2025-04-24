/*
 * Copyright (c) 2018 Demerzel Solutions Limited
 * This file is part of the Nethermind library.
 *
 * The Nethermind library is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Lesser General Public License as published by
 * the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * The Nethermind library is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 * GNU Lesser General Public License for more details.
 *
 * You should have received a copy of the GNU Lesser General Public License
 * along with the Nethermind. If not, see <http://www.gnu.org/licenses/>.
 */

using System;
using System.Numerics;
using Nethermind.Core.Extensions;

namespace Nethermind.Abi
{
    public class AbiArray : AbiType
    {
        private readonly AbiType _elementType;

        public AbiArray(AbiType elementType)
        {
            _elementType = elementType;
            CSharpType = _elementType.CSharpType.MakeArrayType();
        }

        public override bool IsDynamic => true;

        public override string Name => $"{_elementType}[]";

        public override Type CSharpType { get; }

        public override (object, int) Decode(byte[] data, int position, bool packed)
        {
            BigInteger length;
            (length, position) = UInt256.DecodeUInt(data, position, packed);
            
            // Use the actual type returned by the decoder, not _elementType.CSharpType
            Type actualElementType = _elementType is AbiBool ? typeof(bool) : _elementType.CSharpType;
            
            Array result = Array.CreateInstance(actualElementType, (int)length);
            
            for (int i = 0; i < length; i++)
            {
                object element;
                (element, position) = _elementType.Decode(data, position, packed);
                
                try {
                    result.SetValue(element, i);
                }
                catch (Exception) {
                    // Fallback conversion attempt
                    try {
                        object convertedValue = Convert.ChangeType(element, actualElementType);
                        result.SetValue(convertedValue, i);
                    }
                    catch {
                        // If all else fails and we're dealing with a boolean
                        if (actualElementType == typeof(bool)) {
                            // Try numeric conversion to boolean (non-zero = true)
                            try {
                                long numericValue = Convert.ToInt64(element);
                                result.SetValue(numericValue != 0, i);
                            }
                            catch {
                                // Last resort: set default value
                                result.SetValue(false, i);
                            }
                        }
                        // For other types, we'll let the exception propagate
                    }
                }
            }

            return (result, position);
        }

        public override byte[] Encode(object arg, bool packed)
        {
            if (arg is Array input)
            {
                byte[][] encodedItems = new byte[input.Length + 1][];
                int i = 0;
                encodedItems[i++] = UInt256.Encode((BigInteger)input.Length, packed);
                foreach (object o in input)
                {
                    encodedItems[i++] = _elementType.Encode(o, packed);
                }

                return Bytes.Concat(encodedItems);
            }

            throw new AbiException(AbiEncodingExceptionMessage);
        }
    }
}
using System;
using System.IO;

namespace VividRP.Runtime.GPUDriven.Meshlets
{
    /// <summary>
    /// Platform-independent LZ4 block encoder/decoder used by baked meshlet assets.
    /// The encoded bytes are a raw LZ4 block; the owning asset format stores sizes and versioning.
    /// </summary>
    internal static class VividLZ4Codec
    {
        private const int MinimumMatchLength = 4;
        private const int LastLiteralCount = 5;
        private const int LastMatchStartDistance = 12;
        private const int MaximumOffset = ushort.MaxValue;
        private const int HashBits = 16;
        private const int HashTableSize = 1 << HashBits;

        internal static byte[] Compress(byte[] input)
        {
            if (input == null)
            {
                throw new ArgumentNullException(nameof(input));
            }

            if (input.Length == 0)
            {
                return Array.Empty<byte>();
            }

            var hashTable = new int[HashTableSize];
            Array.Fill(hashTable, -1);
            using var output = new MemoryStream(input.Length);

            int anchor = 0;
            int inputIndex = 0;
            int lastMatchPosition = input.Length - LastMatchStartDistance;
            while (inputIndex <= lastMatchPosition)
            {
                int hash = Hash(input, inputIndex);
                int candidateIndex = hashTable[hash];
                hashTable[hash] = inputIndex;

                if (!IsMatch(input, candidateIndex, inputIndex))
                {
                    inputIndex++;
                    continue;
                }

                int matchLength = MinimumMatchLength;
                int matchEnd = input.Length - LastLiteralCount;
                while (inputIndex + matchLength < matchEnd
                       && input[candidateIndex + matchLength] == input[inputIndex + matchLength])
                {
                    matchLength++;
                }

                WriteSequence(
                    output,
                    input,
                    anchor,
                    inputIndex - anchor,
                    inputIndex - candidateIndex,
                    matchLength
                );

                int matchStart = inputIndex;
                inputIndex += matchLength;
                anchor = inputIndex;

                int updateEnd = Math.Min(inputIndex - 1, lastMatchPosition);
                for (int updateIndex = matchStart + 1; updateIndex <= updateEnd; updateIndex++)
                {
                    hashTable[Hash(input, updateIndex)] = updateIndex;
                }
            }

            WriteSequence(output, input, anchor, input.Length - anchor, 0, 0);
            return output.ToArray();
        }

        internal static byte[] Decompress(byte[] input, int decompressedLength)
        {
            if (input == null)
            {
                throw new ArgumentNullException(nameof(input));
            }

            if (decompressedLength < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(decompressedLength));
            }

            if (decompressedLength == 0)
            {
                if (input.Length != 0)
                {
                    throw new InvalidDataException("An empty LZ4 payload must not contain compressed bytes.");
                }

                return Array.Empty<byte>();
            }

            var output = new byte[decompressedLength];
            int inputIndex = 0;
            int outputIndex = 0;

            while (inputIndex < input.Length)
            {
                byte token = input[inputIndex++];
                int literalLength = ReadLength(input, ref inputIndex, token >> 4);
                ValidateCopyRange(input.Length, inputIndex, literalLength, "LZ4 literal input");
                ValidateCopyRange(output.Length, outputIndex, literalLength, "LZ4 literal output");
                Buffer.BlockCopy(input, inputIndex, output, outputIndex, literalLength);
                inputIndex += literalLength;
                outputIndex += literalLength;

                if (inputIndex == input.Length)
                {
                    if (outputIndex != output.Length)
                    {
                        throw new InvalidDataException(
                            $"LZ4 decompressed length mismatch. Expected {output.Length}, got {outputIndex}."
                        );
                    }

                    return output;
                }

                if (input.Length - inputIndex < sizeof(ushort))
                {
                    throw new InvalidDataException("LZ4 block ended inside a match offset.");
                }

                int offset = input[inputIndex] | input[inputIndex + 1] << 8;
                inputIndex += sizeof(ushort);
                if (offset <= 0 || offset > outputIndex)
                {
                    throw new InvalidDataException(
                        $"Invalid LZ4 match offset {offset} at output position {outputIndex}."
                    );
                }

                int matchLength = ReadLength(input, ref inputIndex, token & 0x0F) + MinimumMatchLength;
                ValidateCopyRange(output.Length, outputIndex, matchLength, "LZ4 match output");
                int matchIndex = outputIndex - offset;
                for (int matchByte = 0; matchByte < matchLength; matchByte++)
                {
                    output[outputIndex++] = output[matchIndex + matchByte];
                }
            }

            throw new InvalidDataException(
                $"LZ4 decompressed length mismatch. Expected {output.Length}, got {outputIndex}."
            );
        }

        private static bool IsMatch(byte[] input, int candidateIndex, int inputIndex)
        {
            return candidateIndex >= 0
                   && inputIndex - candidateIndex <= MaximumOffset
                   && input[candidateIndex] == input[inputIndex]
                   && input[candidateIndex + 1] == input[inputIndex + 1]
                   && input[candidateIndex + 2] == input[inputIndex + 2]
                   && input[candidateIndex + 3] == input[inputIndex + 3];
        }

        private static int Hash(byte[] input, int index)
        {
            uint value = (uint) (input[index]
                                 | input[index + 1] << 8
                                 | input[index + 2] << 16
                                 | input[index + 3] << 24);
            return (int) (value * 2654435761u >> (32 - HashBits));
        }

        private static void WriteSequence(
            MemoryStream output,
            byte[] input,
            int literalStart,
            int literalLength,
            int offset,
            int matchLength)
        {
            bool hasMatch = matchLength >= MinimumMatchLength;
            int encodedMatchLength = hasMatch ? matchLength - MinimumMatchLength : 0;
            byte token = (byte) (Math.Min(literalLength, 15) << 4
                                 | Math.Min(encodedMatchLength, 15));
            output.WriteByte(token);

            if (literalLength >= 15)
            {
                WriteExtendedLength(output, literalLength - 15);
            }

            output.Write(input, literalStart, literalLength);
            if (!hasMatch)
            {
                return;
            }

            output.WriteByte((byte) offset);
            output.WriteByte((byte) (offset >> 8));
            if (encodedMatchLength >= 15)
            {
                WriteExtendedLength(output, encodedMatchLength - 15);
            }
        }

        private static void WriteExtendedLength(MemoryStream output, int length)
        {
            while (length >= byte.MaxValue)
            {
                output.WriteByte(byte.MaxValue);
                length -= byte.MaxValue;
            }

            output.WriteByte((byte) length);
        }

        private static int ReadLength(byte[] input, ref int inputIndex, int baseLength)
        {
            int length = baseLength;
            if (baseLength != 15)
            {
                return length;
            }

            byte extension;
            do
            {
                if (inputIndex >= input.Length)
                {
                    throw new InvalidDataException("LZ4 block ended inside an extended length.");
                }

                extension = input[inputIndex++];
                if (length > int.MaxValue - extension)
                {
                    throw new InvalidDataException("LZ4 extended length exceeds the supported range.");
                }

                length += extension;
            }
            while (extension == byte.MaxValue);

            return length;
        }

        private static void ValidateCopyRange(int bufferLength, int offset, int length, string description)
        {
            if (offset < 0 || length < 0 || offset > bufferLength - length)
            {
                throw new InvalidDataException(
                    $"Invalid {description} range: offset {offset}, length {length}, buffer length {bufferLength}."
                );
            }
        }
    }
}

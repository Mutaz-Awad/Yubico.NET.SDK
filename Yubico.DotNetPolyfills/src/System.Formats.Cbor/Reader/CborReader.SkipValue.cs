﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

// Source: https://github.com/dotnet/runtime/tree/v5.0.0-preview.7.20364.11/src/libraries/System.Formats.Cbor/src/System/Formats/Cbor

using System;
using System.Globalization;
using System.Diagnostics;

namespace System.Formats.Cbor
{
    public partial class CborReader
    {
        /// <summary>
        ///   Reads the contents of the next value, discarding the result and advancing the reader.
        /// </summary>
        /// <param name="disableConformanceModeChecks">
        ///   Disable conformance mode validation for the skipped value,
        ///   equivalent to using <see cref="CborConformanceMode.Lax"/>.
        /// </param>
        /// <exception cref="InvalidOperationException">
        ///   the reader is not at the start of new value.
        /// </exception>
        /// <exception cref="CborContentException">
        ///   the next value has an invalid CBOR encoding. -or-
        ///   there was an unexpected end of CBOR encoding data. -or-
        ///   the next value uses a CBOR encoding that is not valid under the current conformance mode.
        /// </exception>
        public void SkipValue(bool disableConformanceModeChecks = false)
        {
            SkipToAncestor(0, disableConformanceModeChecks);
        }

        /// <summary>
        ///   Reads the remaining contents of the current value context,
        ///   discarding results and advancing the reader to the next value
        ///   in the parent context.
        /// </summary>
        /// <param name="disableConformanceModeChecks">
        ///   Disable conformance mode validation for the skipped values,
        ///   equivalent to using <see cref="CborConformanceMode.Lax"/>.
        /// </param>
        /// <exception cref="InvalidOperationException">
        ///   the reader is at the root context
        /// </exception>
        /// <exception cref="CborContentException">
        ///   the next value has an invalid CBOR encoding. -or-
        ///   there was an unexpected end of CBOR encoding data. -or-
        ///   the next value uses a CBOR encoding that is not valid under the current conformance mode.
        /// </exception>
        public void SkipToParent(bool disableConformanceModeChecks = false)
        {
            if (_currentMajorType is null)
            {
                throw new InvalidOperationException(CborExceptionMessages.Cbor_Reader_IsAtRootContext);
            }

            SkipToAncestor(1, disableConformanceModeChecks);
        }

        private void SkipToAncestor(int depth, bool disableConformanceModeChecks)
        {
            Debug.Assert(0 <= depth && depth <= CurrentDepth);
            Checkpoint checkpoint = CreateCheckpoint();
            _isConformanceModeCheckEnabled = !disableConformanceModeChecks;

            try
            {
                do
                {
                    SkipNextNode(ref depth);
                } while (depth > 0);
            }
            catch
            {
                RestoreCheckpoint(in checkpoint);
                throw;
            }
            finally
            {
                _isConformanceModeCheckEnabled = true;
            }
        }

        private void SkipNextNode(ref int depth)
        {
            CborReaderState state;

            // peek, skipping any tags we might encounter
            while ((state = PeekStateCore()) == CborReaderState.Tag)
            {
                _ = ReadTag();
            }

            switch (state)
            {
                case CborReaderState.UnsignedInteger:
                    _ = ReadUInt64();
                    break;

                case CborReaderState.NegativeInteger:
                    _ = ReadCborNegativeIntegerRepresentation();
                    break;

                case CborReaderState.ByteString:
                    SkipString(type: CborMajorType.ByteString);
                    break;

                case CborReaderState.TextString:
                    SkipString(type: CborMajorType.TextString);
                    break;

                case CborReaderState.StartIndefiniteLengthByteString:
                    ReadStartIndefiniteLengthByteString();
                    depth++;
                    break;

                case CborReaderState.EndIndefiniteLengthByteString:
                    ValidatePop(state, depth);
                    ReadEndIndefiniteLengthByteString();
                    depth--;
                    break;

                case CborReaderState.StartIndefiniteLengthTextString:
                    ReadStartIndefiniteLengthTextString();
                    depth++;
                    break;

                case CborReaderState.EndIndefiniteLengthTextString:
                    ValidatePop(state, depth);
                    ReadEndIndefiniteLengthTextString();
                    depth--;
                    break;

                case CborReaderState.StartArray:
                    _ = ReadStartArray();
                    depth++;
                    break;

                case CborReaderState.EndArray:
                    ValidatePop(state, depth);
                    ReadEndArray();
                    depth--;
                    break;

                case CborReaderState.StartMap:
                    _ = ReadStartMap();
                    depth++;
                    break;

                case CborReaderState.EndMap:
                    ValidatePop(state, depth);
                    ReadEndMap();
                    depth--;
                    break;

                case CborReaderState.HalfPrecisionFloat:
                case CborReaderState.SinglePrecisionFloat:
                case CborReaderState.DoublePrecisionFloat:
                    _ = ReadDouble();
                    break;

                case CborReaderState.Null:
                case CborReaderState.Boolean:
                case CborReaderState.SimpleValue:
                    _ = ReadSimpleValue();
                    break;

                default:
                    throw new InvalidOperationException(string.Format(CultureInfo.CurrentCulture, CborExceptionMessages.Cbor_Reader_Skip_InvalidState, state));
            }

            // guards against cases where the caller attempts to skip when reader is not positioned at the start of a value
            static void ValidatePop(CborReaderState state, int depth)
            {
                if (depth == 0)
                {
                    throw new InvalidOperationException(string.Format(CultureInfo.CurrentCulture, CborExceptionMessages.Cbor_Reader_Skip_InvalidState, state));
                }
            }
        }
    }
}

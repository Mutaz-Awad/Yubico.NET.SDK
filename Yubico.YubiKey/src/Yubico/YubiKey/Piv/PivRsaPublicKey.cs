// Copyright 2021 Yubico AB
//
// Licensed under the Apache License, Version 2.0 (the "License").
// You may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System;
using System.Globalization;
using Yubico.Core.Tlv;

namespace Yubico.YubiKey.Piv
{
    /// <summary>
    /// This class holds an RSA public key.
    /// </summary>
    /// <remarks>
    /// An RSA public key consists of a modulus and public exponent. To build an
    /// RSA key object from the modulus and public exponent, use this class's
    /// constructor.
    /// <para>
    /// Once you have the object built, you can get the encoded public key from
    /// either the <c>PivEncodedPublicKey</c> or <c>YubiKeyEncodedPublicKey</c>
    /// property.
    /// </para>
    /// <para>If you have an encoded public key, and want to build an object, use
    /// the static factory <c>Create</c> method in the base class
    /// <see cref="PivPublicKey"/>.
    /// </para>
    /// <para>
    /// The YubiKey supports only 1024-bit and 2048-bit RSA keys, which means
    /// that the modulus must be exactly 128 or 256 byte long.
    /// </para>
    /// <para>
    /// The YubiKey supports only F4 (0x010001 = decimal 65,537) as the public
    /// exponent. Note that if you have the public exponent as an int, you can
    /// convert it to a byte array by using <c>BinaryPrimitives</c>.
    /// <code>
    ///   var exponentAsArray = new byte[4];
    ///   BinaryPrimitives.WriteInt32BigEndian(exponentAsArray, exponentAsInt);
    /// </code>
    /// Unlike the modulus, leading 00 bytes in the public exponent are ignored.
    /// If you want to examine the public exponent as an int, you can either
    /// know that if the class exists, the input was F4 (<c>0x00010001</c>), or
    /// use the <c>BinaryPrimitives.ReadInt32BigEndian</c> method.
    /// </para>
    /// <para>
    /// You can build an object from either the encoded public key (using the
    /// <c>PivPublicKey.Create</c> static factory method), and then examine the
    /// modulus and public exponent, or you can build an object from the modulus
    /// and public exponent, then examine the encoding.
    /// </para>
    /// </remarks>
    public sealed class PivRsaPublicKey : PivPublicKey
    {
        private const int PublicKeyTag = 0x7F49;
        private const int ValidExponentLength = 3;
        private const int ModulusTag = 0x81;
        private const int ExponentTag = 0x82;
        private const int Rsa1024BlockSize = 128;
        private const int Rsa2048BlockSize = 256;
        private const int SliceIndex1024 = 4;
        private const int SliceIndex2048 = 5;
        private const int PublicComponentCount = 2;
        private const int ModulusIndex = 0;
        private const int ExponentIndex = 1;

        private readonly byte[] _exponentF4 = new byte[] { 0x01, 0x00, 0x01 };

        private Memory<byte> _modulus;

        /// <summary>
        /// Contains the modulus portion of the RSA public key.
        /// </summary>
        public ReadOnlySpan<byte> Modulus => _modulus.Span;

        private Memory<byte> _publicExponent;

        /// <summary>
        /// Contains the public exponent portion of the RSA public key.
        /// </summary>
        public ReadOnlySpan<byte> PublicExponent => _publicExponent.Span;

        // The default constructor. We don't want it to be used by anyone outside
        // this class.
        private PivRsaPublicKey()
        {
        }

        /// <summary>
        /// Create a new instance of an RSA public key object based on the
        /// given modulus and public exponent.
        /// </summary>
        /// <param name="modulus">
        /// The modulus as a canonical byte array. It must be 128 bytes (1024
        /// bits) or 256 bytes (2048 bits) long.
        /// </param>
        /// <param name="publicExponent">
        /// The public exponent as a canonical byte array. Note that the YubiKey
        /// supports only <c>0x01 0x00 0x01</c> (aka F4) as a public exponent.
        /// </param>
        /// <exception cref="ArgumentException">
        /// The key data supplied is not supported.
        /// </exception>
        public PivRsaPublicKey(ReadOnlySpan<byte> modulus, ReadOnlySpan<byte> publicExponent)
        {
            if (LoadRsaPublicKey(modulus, publicExponent) == false)
            {
                throw new ArgumentException(
                    string.Format(
                        CultureInfo.CurrentCulture,
                        ExceptionMessages.InvalidPublicKeyData));
            }
        }

        /// <summary>
        /// Try to create a new instance of an RSA public key object based on the
        /// encoding.
        /// </summary>
        /// <remarks>
        /// This static method will build a <c>PivRsaPublicKey</c> object and set
        /// the output parameter <c>publicKeyObject</c> to the resulting key. If
        /// the encoding is not of a supported RSA public key, it will return
        /// false.
        /// </remarks>
        /// <param name="publicKeyObject">
        /// Where the resulting public key object will be deposited.
        /// </param>
        /// <param name="encodedPublicKey">
        /// The PIV TLV encoding.
        /// </param>
        /// <returns>
        /// True if the method was able to create a new RSA public key object,
        /// false otherwise.
        /// </returns>
        internal static bool TryCreate(
            out PivPublicKey publicKeyObject,
            ReadOnlyMemory<byte> encodedPublicKey)
        {
            var returnValue = new PivRsaPublicKey();
            publicKeyObject = returnValue;

            try
            {
                var tlvReader = new TlvReader(encodedPublicKey);
                int tag = tlvReader.PeekTag(2);
                if (tag == PublicKeyTag)
                {
                    tlvReader = tlvReader.ReadNestedTlv(tag);
                }

                var valueArray = new ReadOnlyMemory<byte>[PublicComponentCount];

                while (tlvReader.HasData == true)
                {
                    int valueIndex;
                    tag = tlvReader.PeekTag();
                    switch (tag)
                    {
                        case ModulusTag:
                            valueIndex = ModulusIndex;
                            break;

                        case ExponentTag:
                            valueIndex = ExponentIndex;
                            break;

                        default:
                            return false;
                    }

                    if (valueArray[valueIndex].IsEmpty == false)
                    {
                        return false;
                    }

                    valueArray[valueIndex] = tlvReader.ReadValue(tag);
                }

                return returnValue.LoadRsaPublicKey(
                    valueArray[ModulusIndex].Span, valueArray[ExponentIndex].Span);
            }
            catch (TlvException)
            {
                return false;
            }
        }

        // Load the modulus and exponent and build the encoded key.
        // This method will verify that this class supports the public key given.
        // If successful, return true.
        // If the key given is not supported or the key could not be loaded,
        // return false.
        private bool LoadRsaPublicKey(ReadOnlySpan<byte> modulus, ReadOnlySpan<byte> publicExponent)
        {
            int sliceIndex = SliceIndex1024;
            switch (modulus.Length)
            {
                case Rsa1024BlockSize:
                    Algorithm = PivAlgorithm.Rsa1024;
                    break;

                case Rsa2048BlockSize:
                    Algorithm = PivAlgorithm.Rsa2048;
                    sliceIndex = SliceIndex2048;
                    break;

                default:
                    return false;
            }

            // Make sure the msBit of the modulus is set to verify that the bit
            // length is really 1024 or 2048.
            if ((modulus[0] & 0x80) == 0)
            {
                return false;
            }

            if (IsExponentF4(publicExponent) == false)
            {
                return false;
            }

            var tlvWriter = new TlvWriter();
            using (tlvWriter.WriteNestedTlv(PublicKeyTag))
            {
                tlvWriter.WriteValue(ModulusTag, modulus);
                tlvWriter.WriteValue(ExponentTag, _exponentF4);
            }
            PivEncodedKey = tlvWriter.Encode();

            // The Metadate encoded key is the contents of the nested. So set
            // that to be a slice of the EncodedKey.
            YubiKeyEncodedKey = PivEncodedKey[sliceIndex..];

            _modulus = new Memory<byte>(modulus.ToArray());
            _publicExponent = new Memory<byte>(_exponentF4);

            return true;
        }

        // Is the given exponent 01 00 01?
        // This will allow leading 00 bytes, such as 00 01 00 01.
        private bool IsExponentF4(ReadOnlySpan<byte> exponent)
        {
            if (exponent.Length < ValidExponentLength)
            {
                return false;
            }

            int index = 0;
            while (exponent.Length - index > ValidExponentLength)
            {
                if (exponent[index] != 0)
                {
                    return false;
                }
                index++;
            }

            return exponent.EndsWith<byte>(_exponentF4);
        }
    }
}

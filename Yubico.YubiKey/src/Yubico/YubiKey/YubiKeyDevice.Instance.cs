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
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Yubico.Core.Devices;
using Yubico.Core.Devices.Hid;
using Yubico.Core.Devices.SmartCard;
using Yubico.YubiKey.DeviceExtensions;
using MgmtCmd = Yubico.YubiKey.Management.Commands;

namespace Yubico.YubiKey
{
    public sealed partial class YubiKeyDevice : IYubiKeyDevice
    {
        #region IYubiKeyDeviceInfo
        /// <inheritdoc />
        public YubiKeyCapabilities AvailableUsbCapabilities => _yubiKeyInfo.AvailableUsbCapabilities;

        /// <inheritdoc />
        public YubiKeyCapabilities EnabledUsbCapabilities => _yubiKeyInfo.EnabledUsbCapabilities;

        /// <inheritdoc />
        public YubiKeyCapabilities AvailableNfcCapabilities => _yubiKeyInfo.AvailableNfcCapabilities;

        /// <inheritdoc />
        public YubiKeyCapabilities EnabledNfcCapabilities => _yubiKeyInfo.EnabledNfcCapabilities;

        /// <inheritdoc />
        public int? SerialNumber => _yubiKeyInfo.SerialNumber;

        /// <inheritdoc />
        public bool IsFipsSeries => _yubiKeyInfo.IsFipsSeries;

        /// <inheritdoc />
        public bool IsSkySeries => _yubiKeyInfo.IsSkySeries;

        /// <inheritdoc />
        public FormFactor FormFactor => _yubiKeyInfo.FormFactor;

        /// <inheritdoc />
        public FirmwareVersion FirmwareVersion => _yubiKeyInfo.FirmwareVersion;

        /// <inheritdoc />
        public int AutoEjectTimeout => _yubiKeyInfo.AutoEjectTimeout;

        /// <inheritdoc />
        public byte ChallengeResponseTimeout => _yubiKeyInfo.ChallengeResponseTimeout;

        /// <inheritdoc />
        public DeviceFlags DeviceFlags => _yubiKeyInfo.DeviceFlags;

        /// <inheritdoc />
        public bool ConfigurationLocked => _yubiKeyInfo.ConfigurationLocked;
        #endregion

        private const int _lockCodeLength = MgmtCmd.SetDeviceInfoBaseCommand.LockCodeLength;

        private static readonly ReadOnlyMemory<byte> _lockCodeAllZeros = new byte[_lockCodeLength];

        internal bool HasSmartCard => !(_smartCardDevice is null);
        internal bool HasHidFido => !(_hidFidoDevice is null);
        internal bool HasHidKeyboard => !(_hidKeyboardDevice is null);

        internal bool IsNfcDevice { get; private set; }

        private ISmartCardDevice? _smartCardDevice;
        private IHidDevice? _hidFidoDevice;
        private IHidDevice? _hidKeyboardDevice;
        private IYubiKeyDeviceInfo _yubiKeyInfo;

        /// <summary>
        /// Constructs a <see cref="YubiKeyDevice"/> instance.
        /// </summary>
        /// <param name="device">A valid device; either a smart card, keyboard, or FIDO device.</param>
        /// <param name="info">The YubiKey device information that describes the device.</param>
        /// <exception cref="ArgumentException">An unrecognized device type was given.</exception>
        public YubiKeyDevice(IDevice device, IYubiKeyDeviceInfo info)
        {
            switch (device)
            {
                case ISmartCardDevice scardDevice:
                    _smartCardDevice = scardDevice;
                    break;
                case IHidDevice hidDevice when hidDevice.IsKeyboard():
                    _hidKeyboardDevice = hidDevice;
                    break;
                case IHidDevice hidDevice when hidDevice.IsFido():
                    _hidFidoDevice = hidDevice;
                    break;
                default:
                    throw new ArgumentException(ExceptionMessages.DeviceTypeNotRecognized, nameof(device));
            }

            _yubiKeyInfo = info;
        }

        /// <summary>
        /// Construct a <see cref="YubiKeyDevice"/> instance.
        /// </summary>
        /// <param name="smartCardDevice"><see cref="ISmartCardDevice"/> for the YubiKey.</param>
        /// <param name="hidKeyboardDevice"><see cref="IHidDevice"/> for normal HID interaction with the YubiKey.</param>
        /// <param name="hidFidoDevice"><see cref="IHidDevice"/> for FIDO interaction with the YubiKey.</param>
        /// <param name="yubiKeyDeviceInfo"><see cref="IYubiKeyDeviceInfo"/> with remaining properties of the YubiKey.</param>
        public YubiKeyDevice(
            ISmartCardDevice? smartCardDevice,
            IHidDevice? hidKeyboardDevice,
            IHidDevice? hidFidoDevice,
            IYubiKeyDeviceInfo yubiKeyDeviceInfo)
        {
            _smartCardDevice = smartCardDevice;
            _hidFidoDevice = hidFidoDevice;
            _hidKeyboardDevice = hidKeyboardDevice;
            _yubiKeyInfo = yubiKeyDeviceInfo;
            IsNfcDevice = smartCardDevice?.IsNfcTransport() ?? false;
        }

        /// <summary>
        /// Updates current <see cref="YubiKeyDevice"/> with new info from SmartCard device or HID device.
        /// </summary>
        /// <param name="device"></param>
        /// <param name="info"></param>
        public void Merge(IDevice device, IYubiKeyDeviceInfo info)
        {
            switch (device)
            {
                case ISmartCardDevice scardDevice:
                    _smartCardDevice = scardDevice;
                    IsNfcDevice = scardDevice.IsNfcTransport();
                    break;
                case IHidDevice hidDevice when hidDevice.IsKeyboard():
                    _hidKeyboardDevice = hidDevice;
                    break;
                case IHidDevice hidDevice when hidDevice.IsFido():
                    _hidFidoDevice = hidDevice;
                    break;
                default:
                    throw new ArgumentException(ExceptionMessages.DeviceTypeNotRecognized, nameof(device));
            }

            if (_yubiKeyInfo is YubiKeyDeviceInfo first && info is YubiKeyDeviceInfo second)
            {
                _yubiKeyInfo = first.Merge(second);
            }
            else
            {
                _yubiKeyInfo = info;
            }
        }


        /// <inheritdoc />
        public IYubiKeyConnection Connect(YubiKeyApplication yubikeyApplication)
        {
            if (TryConnect(yubikeyApplication, out IYubiKeyConnection? connection))
            {
                return connection;
            }

            throw new NotSupportedException(ExceptionMessages.NoInterfaceAvailable);
        }

        /// <inheritdoc />
        public IYubiKeyConnection Connect(byte[] applicationId)
        {
            if (TryConnect(applicationId, out IYubiKeyConnection? connection))
            {
                return connection;
            }

            throw new NotSupportedException(ExceptionMessages.NoInterfaceAvailable);
        }

        /// <inheritdoc />
        public bool TryConnect(
            YubiKeyApplication application,
            [MaybeNullWhen(returnValue: false)]
            out IYubiKeyConnection connection)
        {
            // OTP application should prefer the HIDKeyboard transport, but fall back on smart card
            // if unavailable.
            if (application == YubiKeyApplication.Otp && HasHidKeyboard)
            {
                connection = new KeyboardConnection(_hidKeyboardDevice!);
                return true;
            }

            // FIDO applications should prefer the HIDFido transport, but fall back on smart card
            // if unavailable.
            if ((application == YubiKeyApplication.Fido2 || application == YubiKeyApplication.FidoU2f)
                && HasHidFido)
            {
                connection = new FidoConnection(_hidKeyboardDevice!);
                return true;
            }

            if (!HasSmartCard || _smartCardDevice is null)
            {
                connection = null;
                return false;
            }

            connection = new CcidConnection(_smartCardDevice, application);
            return true;
        }

        /// <inheritdoc />
        public bool TryConnect(
            byte[] applicationId,
            [MaybeNullWhen(returnValue: false)]
            out IYubiKeyConnection connection)
        {
            if (!HasSmartCard || _smartCardDevice is null)
            {
                connection = null;
                return false;
            }

            connection = new CcidConnection(_smartCardDevice, applicationId);

            return false;
        }

        /// <inheritdoc/>
        public void SetEnabledNfcCapabilities(YubiKeyCapabilities yubiKeyCapabilities)
        {
            var setCommand = new MgmtCmd.SetDeviceInfoCommand
            {
                EnabledNfcCapabilities = yubiKeyCapabilities,
                ResetAfterConfig = true,
            };

            IYubiKeyResponse setConfigurationResponse = SendConfiguration(setCommand);

            if (setConfigurationResponse.Status != ResponseStatus.Success)
            {
                throw new InvalidOperationException(setConfigurationResponse.StatusMessage);
            }
        }

        /// <inheritdoc/>
        public void SetEnabledUsbCapabilities(YubiKeyCapabilities yubiKeyCapabilities)
        {
            if ((AvailableUsbCapabilities & yubiKeyCapabilities) == YubiKeyCapabilities.None)
            {
                throw new InvalidOperationException(ExceptionMessages.MustEnableOneAvailableUsbCapability);
            }

            var setCommand = new MgmtCmd.SetDeviceInfoCommand
            {
                EnabledUsbCapabilities = yubiKeyCapabilities,
                ResetAfterConfig = true,
            };

            IYubiKeyResponse setConfigurationResponse = SendConfiguration(setCommand);

            if (setConfigurationResponse.Status != ResponseStatus.Success)
            {
                throw new InvalidOperationException(setConfigurationResponse.StatusMessage);
            }
        }

        /// <inheritdoc/>
        public void SetChallengeResponseTimeout(int seconds)
        {
            if (seconds < 0 || seconds > byte.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(seconds));
            }

            var setCommand = new MgmtCmd.SetDeviceInfoCommand
            {
                ChallengeResponseTimeout = (byte)seconds,
            };

            IYubiKeyResponse setConfigurationResponse = SendConfiguration(setCommand);

            if (setConfigurationResponse.Status != ResponseStatus.Success)
            {
                throw new InvalidOperationException(setConfigurationResponse.StatusMessage);
            }
        }

        /// <inheritdoc/>
        public void SetAutoEjectTimeout(int seconds)
        {
            if (seconds < ushort.MinValue || seconds > ushort.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(seconds));
            }

            var setCommand = new MgmtCmd.SetDeviceInfoCommand
            {
                AutoEjectTimeout = seconds,
            };

            IYubiKeyResponse setConfigurationResponse = SendConfiguration(setCommand);

            if (setConfigurationResponse.Status != ResponseStatus.Success)
            {
                throw new InvalidOperationException(setConfigurationResponse.StatusMessage);
            }
        }

        /// <inheritdoc/>
        public void SetDeviceFlags(DeviceFlags deviceFlags)
        {
            var setCommand = new MgmtCmd.SetDeviceInfoCommand
            {
                DeviceFlags = deviceFlags,
            };

            IYubiKeyResponse setConfigurationResponse = SendConfiguration(setCommand);

            if (setConfigurationResponse.Status != ResponseStatus.Success)
            {
                throw new InvalidOperationException(setConfigurationResponse.StatusMessage);
            }
        }

        /// <inheritdoc/>
        public void LockConfiguration(ReadOnlySpan<byte> lockCode)
        {
            if (lockCode.Length != _lockCodeLength)
            {
                throw new ArgumentException(
                    string.Format(
                        CultureInfo.CurrentCulture,
                        ExceptionMessages.LockCodeWrongLength,
                        _lockCodeLength,
                        lockCode.Length),
                        nameof(lockCode));
            }

            if (lockCode.SequenceEqual(_lockCodeAllZeros.Span))
            {
                throw new ArgumentException(
                    ExceptionMessages.LockCodeAllZeroNotAllowed,
                    nameof(lockCode));
            }

            var setCommand = new MgmtCmd.SetDeviceInfoCommand();
            setCommand.SetLockCode(lockCode);

            IYubiKeyResponse setConfigurationResponse = SendConfiguration(setCommand);

            if (setConfigurationResponse.Status != ResponseStatus.Success)
            {
                throw new InvalidOperationException(setConfigurationResponse.StatusMessage);
            }
        }

        /// <inheritdoc/>
        public void UnlockConfiguration(ReadOnlySpan<byte> lockCode)
        {
            if (lockCode.Length != _lockCodeLength)
            {
                throw new ArgumentException(
                    string.Format(
                        CultureInfo.CurrentCulture,
                        ExceptionMessages.LockCodeWrongLength,
                        _lockCodeLength,
                        lockCode.Length),
                        nameof(lockCode));
            }

            var setCommand = new MgmtCmd.SetDeviceInfoCommand();
            setCommand.ApplyLockCode(lockCode);
            setCommand.SetLockCode(_lockCodeAllZeros.Span);

            IYubiKeyResponse setConfigurationResponse = SendConfiguration(setCommand);

            if (setConfigurationResponse.Status != ResponseStatus.Success)
            {
                throw new InvalidOperationException(setConfigurationResponse.StatusMessage);
            }
        }

        /// <inheritdoc/>
        public void SetLegacyDeviceConfiguration(
            YubiKeyCapabilities yubiKeyInterfaces,
            byte challengeResponseTimeout,
            bool touchEjectEnabled,
            int autoEjectTimeout)
        {
            #region argument checks
            // Keep only flags related to interfaces. This makes the operation easier for users
            // who may be doing bitwise operations on [Available/Enabled]UsbCapabilities.
            yubiKeyInterfaces &=
                YubiKeyCapabilities.Ccid
                | YubiKeyCapabilities.FidoU2f
                | YubiKeyCapabilities.Otp;

            // Check if at least one interface is enabled.
            if (yubiKeyInterfaces == YubiKeyCapabilities.None
                || (AvailableUsbCapabilities != YubiKeyCapabilities.None
                && (AvailableUsbCapabilities & yubiKeyInterfaces) == YubiKeyCapabilities.None))
            {
                throw new InvalidOperationException(ExceptionMessages.MustEnableOneAvailableUsbInterface);
            }

            if (touchEjectEnabled)
            {
                if (yubiKeyInterfaces != YubiKeyCapabilities.Ccid)
                {
                    throw new ArgumentException(
                        ExceptionMessages.TouchEjectTimeoutRequiresCcidOnly,
                        nameof(touchEjectEnabled));
                }

                if (autoEjectTimeout < ushort.MinValue || autoEjectTimeout > ushort.MaxValue)
                {
                    throw new ArgumentOutOfRangeException(nameof(autoEjectTimeout));
                }
            }
            else
            {
                if (autoEjectTimeout != 0)
                {
                    throw new ArgumentException(
                        ExceptionMessages.AutoEjectTimeoutRequiresTouchEjectEnabled,
                        nameof(autoEjectTimeout));
                }
            }
            #endregion

            IYubiKeyResponse setConfigurationResponse;

            // Newer YubiKeys should use SetDeviceInfo
            if (FirmwareVersion.Major >= 5)
            {
                DeviceFlags deviceFlags =
                    touchEjectEnabled
                    ? DeviceFlags | DeviceFlags.TouchEject
                    : DeviceFlags & ~DeviceFlags.TouchEject;

                var setDeviceInfoCommand = new MgmtCmd.SetDeviceInfoCommand
                {
                    EnabledUsbCapabilities = yubiKeyInterfaces.ToDeviceInfoCapabilities(),
                    ChallengeResponseTimeout = challengeResponseTimeout,
                    AutoEjectTimeout = autoEjectTimeout,
                    DeviceFlags = deviceFlags,
                    ResetAfterConfig = true,
                };

                setConfigurationResponse = SendConfiguration(setDeviceInfoCommand);
            }
            else
            {
                var setLegacyDeviceConfigCommand = new MgmtCmd.SetLegacyDeviceConfigCommand(
                    yubiKeyInterfaces,
                    challengeResponseTimeout,
                    touchEjectEnabled,
                    autoEjectTimeout);

                setConfigurationResponse = SendConfiguration(setLegacyDeviceConfigCommand);
            }

            if (setConfigurationResponse.Status != ResponseStatus.Success)
            {
                throw new InvalidOperationException(setConfigurationResponse.StatusMessage);
            }
        }

        private IYubiKeyResponse SendConfiguration(MgmtCmd.SetDeviceInfoBaseCommand baseCommand)
        {
            IYubiKeyConnection? connection = null;
            try
            {
                IYubiKeyCommand<IYubiKeyResponse> command;

                if (TryConnect(YubiKeyApplication.Management, out connection))
                {
                    command = new MgmtCmd.SetDeviceInfoCommand(baseCommand);
                }
                else if (TryConnect(YubiKeyApplication.Otp, out connection))
                {
                    command = new Otp.Commands.SetDeviceInfoCommand(baseCommand);
                }
                else
                {
                    throw new NotSupportedException(ExceptionMessages.NoInterfaceAvailable);
                }

                return connection.SendCommand(command);
            }
            finally
            {
                connection?.Dispose();
            }
        }

        private IYubiKeyResponse SendConfiguration(
            MgmtCmd.SetLegacyDeviceConfigBase baseCommand)
        {
            IYubiKeyConnection? connection = null;
            try
            {
                IYubiKeyCommand<IYubiKeyResponse> command;

                if (TryConnect(YubiKeyApplication.Management, out connection))
                {
                    command = new MgmtCmd.SetLegacyDeviceConfigCommand(baseCommand);
                }
                else if (TryConnect(YubiKeyApplication.Otp, out connection))
                {
                    command = new Otp.Commands.SetLegacyDeviceConfigCommand(baseCommand);
                }
                else
                {
                    throw new NotSupportedException(ExceptionMessages.NoInterfaceAvailable);
                }

                return connection.SendCommand(command);
            }
            finally
            {
                connection?.Dispose();
            }
        }

        #region IEquatable<T> and IComparable<T>
        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            var other = obj as IYubiKeyDevice;
            if (other == null)
            {
                return false;
            }
            else
            {
                return Equals(other);
            }
        }

        /// <inheritdoc/>
        public bool Equals(IYubiKeyDevice other)
        {
            if (this is null && other is null)
            {
                return true;
            }

            if (this is null || other is null)
            {
                return false;
            }

            if (ReferenceEquals(this, other))
            {
                return true;
            }

            if (SerialNumber == null && other.SerialNumber == null)
            {
                // fingerprint match
                if (!Equals(FirmwareVersion, other.FirmwareVersion))
                {
                    return false;
                }

                return CompareTo(other) == 0;
            }
            else if (SerialNumber == null || other.SerialNumber == null)
            {
                return false;
            }
            else
            {
                return SerialNumber.Equals(other.SerialNumber);
            }
        }

        /// <inheritdoc/>
        bool IYubiKeyDevice.Contains(IDevice other) =>
            other switch
            {
                ISmartCardDevice scDevice => scDevice.Path == _smartCardDevice?.Path,
                IHidDevice hidDevice => hidDevice.Path == _hidKeyboardDevice?.Path ||
                                        hidDevice.Path == _hidFidoDevice?.Path,
                _ => false
            };

        /// <inheritdoc/>
        public int CompareTo(IYubiKeyDevice other)
        {
            if (other is null)
            {
                throw new ArgumentNullException(nameof(other));
            }

            if (ReferenceEquals(this, other))
            {
                return 0;
            }

            if (SerialNumber == null && other.SerialNumber == null)
            {
                var concreteKey = other as YubiKeyDevice;

                if (concreteKey is null)
                {
                    return 1;
                }

                if (HasSmartCard)
                {
                    int delta = string.Compare(_smartCardDevice!.Path, concreteKey._smartCardDevice!.Path, StringComparison.Ordinal);
                    if (delta != 0)
                    {
                        return delta;
                    }
                }
                else if (concreteKey.HasSmartCard)
                {
                    return -1;
                }

                if (HasHidFido)
                {
                    int delta = string.Compare(_hidFidoDevice!.Path, concreteKey._hidFidoDevice!.Path, StringComparison.Ordinal);
                    if (delta != 0)
                    {
                        return delta;
                    }
                }
                else if (concreteKey.HasHidFido)
                {
                    return -1;
                }

                if (HasHidKeyboard)
                {
                    int delta = string.Compare(_hidKeyboardDevice!.Path, concreteKey._hidKeyboardDevice!.Path, StringComparison.Ordinal);
                    if (delta != 0)
                    {
                        return delta;
                    }
                }
                else if (concreteKey.HasHidFido)
                {
                    return -1;
                }

                return 0;
            }
            else if (SerialNumber == null)
            {
                return -1;
            }
            else if (other.SerialNumber == null)
            {
                return 1;
            }
            else
            {
                return SerialNumber.Value.CompareTo(other.SerialNumber.Value);
            }
        }

        /// <inheritdoc/>
        public static bool operator ==(YubiKeyDevice left, YubiKeyDevice right)
        {
            if (left is null)
            {
                return right is null;
            }

            return left.Equals(right);
        }

        /// <inheritdoc/>
        public static bool operator !=(YubiKeyDevice left, YubiKeyDevice right) => !(left == right);

        /// <inheritdoc/>
        public static bool operator <(YubiKeyDevice left, YubiKeyDevice right) =>
            left is null ? right is object : left.CompareTo(right) < 0;

        /// <inheritdoc/>
        public static bool operator <=(YubiKeyDevice left, YubiKeyDevice right) =>
            left is null || left.CompareTo(right) <= 0;

        /// <inheritdoc/>
        public static bool operator >(YubiKeyDevice left, YubiKeyDevice right) =>
            left is object && left.CompareTo(right) > 0;

        /// <inheritdoc/>
        public static bool operator >=(YubiKeyDevice left, YubiKeyDevice right) =>
            left is null ? right is null : left.CompareTo(right) >= 0;
        #endregion

        #region System.Object overrides
        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return !(SerialNumber is null)
                ? SerialNumber!.GetHashCode()
                : HashCode.Combine(
                    _smartCardDevice?.Path,
                    _hidFidoDevice?.Path,
                    _hidKeyboardDevice?.Path);
        }

        private static readonly string EOL = Environment.NewLine;
        /// <inheritdoc/>
        public override string ToString()
        {
            string res = "- Firmware Version: " + FirmwareVersion + EOL
                + "- Serial Number: " + SerialNumber + EOL
                + "- Form Factor: " + FormFactor + EOL
                + "- FIPS: " + IsFipsSeries + EOL
                + "- SKY: " + IsSkySeries + EOL
                + "- Has SmartCard: " + HasSmartCard + EOL
                + "- Has HID FIDO: " + HasHidFido + EOL
                + "- Has HID Keyboard: " + HasHidKeyboard + EOL
                + "- Available USB Capabilities: " + AvailableUsbCapabilities + EOL
                + "- Available NFC Capabilities: " + AvailableNfcCapabilities + EOL
                + "- Enabled USB Capabilities: " + EnabledUsbCapabilities + EOL
                + "- Enabled NFC Capabilities: " + EnabledNfcCapabilities + EOL;

            return res;
        }
        #endregion
    }
}

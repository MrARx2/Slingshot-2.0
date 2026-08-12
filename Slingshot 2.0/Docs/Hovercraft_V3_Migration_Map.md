# Hovercraft V3 Readability Sprint Migration Map

All moves preserved the original file or directory `.meta` file unless the
destination was a newly created organizational folder.

## Runtime core

| Old path | New path |
|---|---|
| `Runtime/Assembly/ConnectorChildMount.cs` | `Runtime/Core/Assembly/ConnectorChildMount.cs` |
| `Runtime/Assembly/V3Socket.cs` | `Runtime/Core/Assembly/V3Socket.cs` |
| `Runtime/Build/CraftBuildDefinition.cs` | `Runtime/Core/Build/CraftBuildDefinition.cs` |
| `Runtime/Build/CraftBuildValidator.cs` | `Runtime/Core/Build/CraftBuildValidator.cs` |
| `Runtime/Runtime/V3CraftAssembler.cs` | `Runtime/Core/Assembly/V3CraftAssembler.cs` |
| `Runtime/Runtime/V3CraftRuntime.cs` | `Runtime/Core/Assembly/V3CraftRuntime.cs` |
| `Runtime/Runtime/V3CraftMassCalculator.cs` | `Runtime/Core/Assembly/V3CraftMassCalculator.cs` |
| `Runtime/Runtime/V3CraftMainframe.cs` | `Runtime/Core/Mainframe/V3CraftMainframe.cs` |
| `Runtime/Runtime/V3ChassisObservationProvider.cs` | `Runtime/Core/Mainframe/V3ChassisObservationProvider.cs` |
| `Runtime/Data/V3MainframeDefinition.cs` | `Runtime/Core/Mainframe/V3MainframeDefinition.cs` |
| `Runtime/Runtime/V3MainframeContracts.cs` | `Runtime/Core/Messaging/V3MainframeContracts.cs` |
| `Runtime/Runtime/V3SoftwareScheduler.cs` | `Runtime/Core/Scheduling/V3SoftwareScheduler.cs` |
| `Runtime/Data/V3SystemSoftwareDefinition.cs` | `Runtime/Core/Software/V3SystemSoftwareDefinition.cs` |
| `Runtime/Runtime/V3DeviceContracts.cs` | `Runtime/Core/Firmware/V3DeviceContracts.cs` |
| `Runtime/Data/V3DeviceFirmwareDefinition.cs` | `Runtime/Core/Firmware/V3DeviceFirmwareDefinition.cs` |
| `Runtime/Runtime/V3ControlRouter.cs` | `Runtime/Core/Routing/V3ControlRouter.cs` |
| `Runtime/Runtime/V3ActuatorCommandRouter.cs` | `Runtime/Core/Routing/V3ActuatorCommandRouter.cs` |
| `Runtime/Runtime/V3ControllerPipeline.cs` | `Runtime/Core/Routing/V3ControllerPipeline.cs` |
| `Runtime/Data/V3RouterProfileDefinition.cs` | `Runtime/Core/Routing/V3RouterProfileDefinition.cs` |
| `Runtime/Runtime/V3PowerDistributor.cs` | `Runtime/Core/Power/V3PowerDistributor.cs` |
| `Runtime/Runtime/V3PowerModeInfo.cs` | `Runtime/Core/Power/V3PowerModeInfo.cs` |
| `Runtime/Runtime/V3ThermalController.cs` | `Runtime/Core/Thermal/V3ThermalController.cs` |
| `Runtime/Runtime/V3CraftRegistries.cs` | `Runtime/Core/Registries/V3CraftRegistries.cs` |
| `Runtime/Runtime/V3RuntimeBuildValidator.cs` | `Runtime/Core/Validation/V3RuntimeBuildValidator.cs` |
| `Runtime/Runtime/V3CraftTelemetryHub.cs` | `Runtime/Core/Telemetry/V3CraftTelemetryHub.cs` |

## Part definitions and runtimes

| Old path | New path |
|---|---|
| `Runtime/Data/PartDefinition.cs` | `Runtime/Parts/Common/PartDefinition.cs` |
| `Runtime/Data/ManufacturerDefinition.cs` | `Runtime/Parts/Common/ManufacturerDefinition.cs` |
| `Runtime/Data/PartCatalog.cs` | `Runtime/Parts/Common/PartCatalog.cs` |
| `Runtime/Data/V3AssemblyTypes.cs` | `Runtime/Parts/Common/V3AssemblyTypes.cs` |
| `Runtime/Runtime/RuntimePartInstance.cs` | `Runtime/Parts/Common/RuntimePartInstance.cs` |
| `Runtime/Data/ChassisDefinition.cs` | `Runtime/Parts/Chassis/ChassisDefinition.cs` |
| `Runtime/Data/CockpitDefinition.cs` | `Runtime/Parts/Cockpits/CockpitDefinition.cs` |
| `Runtime/Data/InternalSystemDefinition.cs` | `Runtime/Parts/Computers/InternalSystemDefinition.cs` |
| `Runtime/Data/V3SystemComputerDefinition.cs` | `Runtime/Parts/Computers/V3SystemComputerDefinition.cs` |
| `Runtime/Data/ThrusterDefinition.cs` | `Runtime/Parts/Thrusters/ThrusterDefinition.cs` |
| `Runtime/Runtime/RuntimeThrusterInstance.cs` | `Runtime/Parts/Thrusters/RuntimeThrusterInstance.cs` |
| `Runtime/Data/ConnectorDefinition.cs` | `Runtime/Parts/Connectors/ConnectorDefinition.cs` |
| `Runtime/Data/FixedAdapterDefinition.cs` | `Runtime/Parts/Connectors/FixedAdapterDefinition.cs` |
| `Runtime/Data/GimbalDefinition.cs` | `Runtime/Parts/Connectors/Gimbals/GimbalDefinition.cs` |
| `Runtime/Runtime/RuntimeGimbalInstance.cs` | `Runtime/Parts/Connectors/Gimbals/RuntimeGimbalInstance.cs` |
| `Runtime/Data/SpringMountDefinition.cs` | `Runtime/Parts/Connectors/Springs/SpringMountDefinition.cs` |
| `Runtime/Runtime/RuntimeSpringMountInstance.cs` | `Runtime/Parts/Connectors/Springs/RuntimeSpringMountInstance.cs` |
| `Runtime/Data/V3RotaryActuatorDefinition.cs` | `Runtime/Parts/Connectors/RotaryActuators/V3RotaryActuatorDefinition.cs` |
| `Runtime/Data/V3AerodynamicFinDefinition.cs` | `Runtime/Parts/Aerodynamics/V3AerodynamicFinDefinition.cs` |
| `Runtime/Runtime/RuntimeAerodynamicFinInstance.cs` | `Runtime/Parts/Aerodynamics/RuntimeAerodynamicFinInstance.cs` |
| `Runtime/Runtime/V3ActiveAeroRuntime.cs` | `Runtime/Parts/Aerodynamics/V3ActiveAeroRuntime.cs` |
| `Runtime/Data/V3DirectionalSensorDefinition.cs` | `Runtime/Parts/Sensors/V3DirectionalSensorDefinition.cs` |
| `Runtime/Runtime/V3DirectionalSensorRuntime.cs` | `Runtime/Parts/Sensors/V3DirectionalSensorRuntime.cs` |
| `Runtime/Data/EnergyCoreDefinition.cs` | `Runtime/Parts/EnergyCores/EnergyCoreDefinition.cs` |
| `Runtime/Data/V3CoolingModuleDefinition.cs` | `Runtime/Parts/Cooling/V3CoolingModuleDefinition.cs` |
| `Runtime/Runtime/V3CoolingRuntime.cs` | `Runtime/Parts/Cooling/V3CoolingRuntime.cs` |

## Systems, input, presentation, and development

| Old path | New path |
|---|---|
| `Runtime/Runtime/V3DriveController.cs` | `Runtime/Systems/Drive/V3DriveController.cs` |
| `Runtime/Runtime/V3HoverController.cs` | `Runtime/Systems/Hover/V3HoverController.cs` |
| `Runtime/Runtime/V3TractionController.cs` | `Runtime/Systems/Traction/V3TractionController.cs` |
| `Runtime/Runtime/V3VectorController.cs` | `Runtime/Systems/Stabilization/V3VectorController.cs` |
| `Runtime/Runtime/V3GimbalController.cs` | `Runtime/Systems/Stabilization/V3GimbalController.cs` |
| `Runtime/Runtime/V3PilotCommand.cs` | `Runtime/Input/V3PilotCommand.cs` |
| `Runtime/Runtime/V3PilotInputAdapter.cs` | `Runtime/Input/V3PilotInputAdapter.cs` |
| `Runtime/Runtime/V3FreeDriveCamera.cs` | `Runtime/Presentation/Camera/V3FreeDriveCamera.cs` |
| `Runtime/Runtime/V3FreeDriveHud.cs` | `Runtime/Presentation/UI/V3FreeDriveHud.cs` |
| `Runtime/Runtime/V3CockpitWarningDisplay.cs` | `Runtime/Presentation/UI/V3CockpitWarningDisplay.cs` |
| `Runtime/Runtime/V3SystemsDebugDisplay.cs` | `Runtime/Presentation/Debug/V3SystemsDebugDisplay.cs` |
| `Runtime/Runtime/V3ThermalDebugDisplay.cs` | `Runtime/Presentation/Debug/V3ThermalDebugDisplay.cs` |
| `Runtime/Runtime/V3ThrusterDebugView.cs` | `Runtime/Presentation/Debug/V3ThrusterDebugView.cs` |
| `Runtime/Runtime/V3CockpitWarningController.cs` | `Runtime/Presentation/Warnings/V3CockpitWarningController.cs` |
| `Runtime/Runtime/V3FreeDriveSession.cs` | `Runtime/Development/TestDrive/V3FreeDriveSession.cs` |
| `Runtime/Runtime/V3ParityCommandProfile.cs` | `Runtime/Development/Regression/V3ParityCommandProfile.cs` |
| `Runtime/Runtime/V3ParityRunController.cs` | `Runtime/Development/Regression/V3ParityRunController.cs` |
| `Runtime/Runtime/V3ParityRunData.cs` | `Runtime/Development/Regression/V3ParityRunData.cs` |

## Editor and tests

| Old path | New path |
|---|---|
| `Editor/CraftBuilderWindow.cs` | `Editor/Authoring/CraftBuilderWindow.cs` |
| `Editor/V3CraftAssemblerEditor.cs` | `Editor/Inspectors/V3CraftAssemblerEditor.cs` |
| `Editor/V2ReferencePrototypeGenerator.cs` | `Editor/Generation/V2ReferencePrototypeGenerator.cs` |
| `Editor/V3ParityHarnessGenerator.cs` | `Editor/Generation/V3ParityHarnessGenerator.cs` |
| `Editor/V3SystemsIntegrationPrototypeGenerator.cs` | `Editor/Generation/V3SystemsIntegrationPrototypeGenerator.cs` |
| `Editor/V3FreeDriveSceneGenerator.cs` | `Editor/SceneTools/V3FreeDriveSceneGenerator.cs` |
| `Tests/Editor/CraftBuildValidatorTests.cs` | `Tests/Editor/Core/CraftBuildValidatorTests.cs` |
| `Tests/Editor/V3CraftKernelServicesTests.cs` | `Tests/Editor/Core/V3CraftKernelServicesTests.cs` |
| `Tests/Editor/V3CraftRegistryTests.cs` | `Tests/Editor/Core/V3CraftRegistryTests.cs` |
| `Tests/Editor/V3ThermalFoundationTests.cs` | `Tests/Editor/Core/V3ThermalFoundationTests.cs` |
| `Tests/Editor/V3GimbalVariationTests.cs` | `Tests/Editor/Parts/V3GimbalVariationTests.cs` |
| `Tests/Editor/V3SpringMountVariationTests.cs` | `Tests/Editor/Parts/V3SpringMountVariationTests.cs` |
| `Tests/Editor/V3HandlingControllerTests.cs` | `Tests/Editor/Systems/V3HandlingControllerTests.cs` |
| `Tests/Editor/V3MainframeFoundationTests.cs` | `Tests/Editor/Systems/V3MainframeFoundationTests.cs` |
| `Tests/Editor/V3OverloadProtectionTests.cs` | `Tests/Editor/Systems/V3OverloadProtectionTests.cs` |
| `Tests/Editor/V3PowerDistributionModeTests.cs` | `Tests/Editor/Systems/V3PowerDistributionModeTests.cs` |
| `Tests/Editor/V3CraftTelemetryHubTests.cs` | `Tests/Editor/Integration/V3CraftTelemetryHubTests.cs` |
| `Tests/Editor/V3PlayerFeedbackAndRecoveryTests.cs` | `Tests/Editor/Integration/V3PlayerFeedbackAndRecoveryTests.cs` |
| `Tests/Editor/V3SystemsIntegrationTests.cs` | `Tests/Editor/Integration/V3SystemsIntegrationTests.cs` |
| `Tests/Editor/V2ReferencePrototypeTests.cs` | `Tests/Editor/Regression/V2ReferencePrototypeTests.cs` |
| `Tests/Editor/V3ParityHarnessTests.cs` | `Tests/Editor/Regression/V3ParityHarnessTests.cs` |

The runtime, editor, and test asmdefs remain at their original assembly roots so
the migration does not change assembly boundaries.

## Generated assets and scenes

| Old path | New path |
|---|---|
| `Prototype/` | `Generated/ReferenceCrafts/V2Parity/` |
| `SystemsIntegrationPrototype/` | `Generated/ReferenceCrafts/SystemsIntegration/` |
| `Parity/` | `Generated/RegressionAssets/Parity/` |
| `FreeDrive/Scenes/V3_FreeDrive_Test.unity` | `Scenes/Development/V3_HandlingTrack.unity` |
| `SystemsIntegrationPrototype/Scenes/V3_SystemsIntegration_Test.unity` | `Scenes/Development/V3_CraftLab.unity` |
| `FreeDrive/Materials/` | `Scenes/Shared/Materials/` |

The corresponding generators now own only these canonical paths.

# PSVR2ToolkitTriggerConfig
A simple and lazy app to test setting the parameters of the PSVR2 adaptive triggers, using PSVR2Toolkit. 

## Prerequisites

Before using this tool, you'll need to have [PSVR2Toolkit](https://github.com/BnuuySolutions/PSVR2Toolkit/) installed. 

## How to Use

1.  Start SteamVR.
2.  Ensure your controllers are on and connected.
4.  Run **PSVR2TKTriggerConfig.exe**. The status bar at the bottom should update from "Connecting..." to "Connected".
5.  Use the tool to adjust the trigger parameters. The effects will be applied immediately to your controllers.

## Features

*   Live, real-time application of trigger effects as you adjust sliders.
*   Support for all trigger effects exposed by the IPC library.
*   Ability to save and load effect presets to JSON files.
*   Editable text values for precise parameter input. Double-click the fields to make them editable. 
*   Mouse-wheel support for adjusting sliders.

## Troubleshooting

- **Status says "Disconnected":** Make sure SteamVR is running and PSVR2Toolkit is correctly installed.
- **Status says "Connected" but triggers have no effect:** Check that your controllers are on and connected. They don't need to be tracking. 

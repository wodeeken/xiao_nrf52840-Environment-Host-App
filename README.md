# xiao_nrf52840-Environment-Host-App

October 21, 2024 - WIP - .NET 8.0 console application for collecting environmental data (air temperature/pressure/humidity, sensor enclosure temperature, camera,audio) from the xiao_nrf52840-Environment-Transmitter project (https://github.com/wodeeken/xiao_nrf52840-Environment-Transmitter) using Bluetooth.

# Prerequisites
1. Only Linux running the BlueZ Bluetooth stack is supported. This program cannot run on Windows.
2. Linux tar is required to perform old data archival operations.
3. SoX must be installed on the system to perform raw audio conversion. If not already installed, it can be installed with the "apt install sox" command.


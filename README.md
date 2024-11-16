# CIBM
## Console app
It alow to stream CIBM 107.1 radio from a console app:
**https://www.cibm107.com**

## Web server
Allowing request to http://XXX.XXX.XXX.XXX:80 to control the radio stream
| Path | Description |
| ---- | ------- |
| /PlayPause | Play or pause the stream |
| /GetState | Get current status of server |
| /GetVolume | Get the volume in % |
| /SetVolume?percent=XXX | Set the volume in % |
| /Sync | Sync the local audio to live stream |

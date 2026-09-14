# September Server

September Server is a server implementation primarily tested against an **September 15th, 2016 build of Rec Room**.

From what I investigated, this server should work from a version from August 30, 2016 to a version from November 9, 2016, as they added the account system in that version!

Make sure in the server folder you have a /content folder, and inside the /content folder there should be a motd.txt with what you want the MOTD to be!

## Building

Make sure you have **.NET 9.0** installed.

### 1. Clone the repository

```bash
git clone https://github.com/bassmentrr/August-Server.git
cd August-Server
```
or if your lazy, just download the zip from github!

### 2. Build the server

```bash
dotnet build
```

### 3. Run the server

```bash
dotnet run
```

The server should run on port 6000!

I don't recall why I chose port 6000 but oh well, it's easy to change.

## Releases

Pre-built versions of the server are available from the **Releases** page!

They are **Windows x64** only however.

The pre-built versions ***should*** be self-contained unless I screwed something up!
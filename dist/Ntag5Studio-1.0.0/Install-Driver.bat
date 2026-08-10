::install driver

pushd %~dp0
pnputil.exe /add-driver NFC/SpbNfc.inf /install


popd
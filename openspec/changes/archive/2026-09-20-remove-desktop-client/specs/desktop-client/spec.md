## REMOVED Requirements

Every requirement of this capability is removed. The reason and the migration are the same for all
eighteen, and are stated once here rather than repeated under each:

**Reason**: The Avalonia desktop client is deleted. Every rule the ledger enforces had to be
restated in two clients, and the desktop one is not used; a capability spec describes the system as
it is, so it goes with the code.

**Migration**: Use the browser client, which offers the same launcher and
capture-review-confirm flow over the same HTTP interface. Nothing in the ledger changes, so a
desktop client could be rebuilt against the unchanged interface if one is ever wanted again; the
deleted code remains in git history.

### Requirement: The client reaches the ledger at a configured address

**Reason**: See above — the client is deleted.

**Migration**: The browser client's equivalent requirement, "The ledger address is configured when
the client is served" in `browser-client`, covers the surviving client.

### Requirement: The client leads with capture

**Reason**: See above — the client is deleted.

**Migration**: `browser-client`, "The client leads with capture".

### Requirement: Capture takes an image from a file or a drop

**Reason**: See above — the client is deleted. This requirement has no browser-client counterpart:
taking an image from a file or a drop was the desktop client's reason for existing, and the browser
client opens the camera instead.

**Migration**: Photograph the receipt with the browser client on a phone, or hand its file input an
image file on a desktop browser.

### Requirement: A captured image is carried forward unmodified

**Reason**: See above — the client is deleted.

**Migration**: `browser-client`, "A captured image is carried forward unmodified".

### Requirement: Recent purchases are listed for recognition

**Reason**: See above — the client is deleted.

**Migration**: `browser-client`, "Recent purchases are listed for recognition".

### Requirement: Display names are resolved from reference data

**Reason**: See above — the client is deleted.

**Migration**: `browser-client`, "Display names are resolved from reference data".

### Requirement: The month to date is reported as a single figure

**Reason**: See above — the client is deleted.

**Migration**: `browser-client`, "The month to date is reported as a single figure".

### Requirement: Receipts needing review are surfaced

**Reason**: See above — the client is deleted.

**Migration**: `browser-client`, "Receipts needing review are surfaced".

### Requirement: Failure to read the ledger degrades rather than breaks

**Reason**: See above — the client is deleted.

**Migration**: `browser-client`, "Failure to read the ledger degrades rather than breaks".

### Requirement: The client fits the window it is shown in

**Reason**: See above — the client is deleted. The browser client states its own layout rules for
a phone viewport rather than a resizable desktop window.

**Migration**: `browser-client`'s layout requirements.

### Requirement: Amounts and dates are formatted for reading

**Reason**: See above — the client is deleted.

**Migration**: `browser-client`, "Amounts and dates are formatted for reading".

### Requirement: A captured image is uploaded on arrival

**Reason**: See above — the client is deleted.

**Migration**: `browser-client`, "A captured image is uploaded on arrival".

### Requirement: A successful capture is presented for review

**Reason**: See above — the client is deleted.

**Migration**: `browser-client`, "A successful capture is presented for review".

### Requirement: Extraction problems are surfaced before confirmation

**Reason**: See above — the client is deleted.

**Migration**: `browser-client`, "Extraction problems are surfaced before confirmation".

### Requirement: A failed extraction still allows manual entry

**Reason**: See above — the client is deleted.

**Migration**: `browser-client`, "A failed extraction still allows manual entry".

### Requirement: Confirming a capture creates the purchase

**Reason**: See above — the client is deleted.

**Migration**: `browser-client`, "Confirming a capture creates the purchase".

### Requirement: A rejected confirmation is reported in place

**Reason**: See above — the client is deleted.

**Migration**: `browser-client`, "A rejected confirmation is reported in place".

### Requirement: Abandoning a capture leaves no trace

**Reason**: See above — the client is deleted.

**Migration**: `browser-client`, "Abandoning a capture leaves no trace".

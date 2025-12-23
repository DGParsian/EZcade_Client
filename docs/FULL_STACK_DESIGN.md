# EZcade_Client - Full Stack Design Document

## Document Overview

**Project**: EZcade_Client  
**Technology Stack**: C# .NET 8, WPF, HttpClient, TCP Sockets  
**Version**: 1.0  
**Last Updated**: 2024  
**Focus**: KEY FEATURES & LOGICAL FLOWS

This document provides a comprehensive design overview of the EZcade_Client desktop application, with primary emphasis on **core features** and **logical workflows**. The application serves as an operator interface between the EZcade hardware system and a centralized server, managing serial number generation, component tracking, and hardware activation workflows.

---

## Table of Contents

1. [Core Features (Detailed)](#1-core-features-detailed)
   - 1.1 [Authentication & Credential Management](#11-authentication--credential-management)
   - 1.2 [Batch Selection System](#12-batch-selection-system)
   - 1.3 [Serial Number Generation & Query System](#13-serial-number-generation--query-system)
   - 1.4 [Hardware Communication Workflow](#14-hardware-communication-workflow)
   - 1.5 [Manual vs Automated Processing Modes](#15-manual-vs-automated-processing-modes)
   - 1.6 [Data Matrix Code Handling](#16-data-matrix-code-handling)
   - 1.7 [Product/PCB Activation Workflow](#17-productpcb-activation-workflow)

2. [Logical Flows & Workflows (Detailed Sequences)](#2-logical-flows--workflows-detailed-sequences)
   - 2.1 [Application Startup Flow](#21-application-startup-flow)
   - 2.2 [Hardware Request Processing Flow (Manual Mode)](#22-hardware-request-processing-flow-manual-mode)
   - 2.3 [Automated Query Batch Processing Flow](#23-automated-query-batch-processing-flow)
   - 2.4 [Data Transformation Pipeline](#24-data-transformation-pipeline)
   - 2.5 [Error & Exception Flows](#25-error--exception-flows)

3. [State Management & UI Transitions](#3-state-management--ui-transitions)
4. [Configuration & Parameters](#4-configuration--parameters)
5. [Architecture Components](#5-architecture-components)
6. [Future Considerations](#6-future-considerations)

---

## 1. Core Features (Detailed)

### 1.1 Authentication & Credential Management

#### Feature Overview
The application implements a token-based authentication system that communicates with a centralized REST API server. Authentication credentials are persisted locally to enable automatic login on subsequent application launches.

#### Key Components
- **HttpConnectionHandler**: Manages HTTP client and token storage
- **LoginWindow**: UI for manual credential entry
- **Properties.Settings**: Persists SavedUsername and SavedPassword

#### Authentication Flow Details

**Login Process:**
1. User credentials (username/password) sent via HTTP POST to `/login` endpoint
2. Server validates credentials and returns JSON response containing `myToken`
3. Token extracted and stored in `_token` field
4. Token added to HttpClient default headers as "Authorization" header
5. All subsequent requests automatically include this token

**Credential Persistence:**
- Successful login triggers save to `Properties.Settings`
- Settings stored in user-specific application data directory
- Plaintext storage (security consideration for future improvement)

**Auto-Login Mechanism:**
1. On application startup, `MainWindow` constructor calls `AutoLogin()`
2. Retrieves SavedUsername and SavedPassword from Settings
3. If both exist, attempts login via `HttpConnectionHandler.LoginAsync()`
4. Success: Application proceeds to MainWindow_Loaded
5. Failure: LoginWindow shown as modal dialog (blocks until login succeeds)

**Fallback Flow:**
- If auto-login fails or credentials don't exist, LoginWindow displayed
- User must manually enter credentials
- Upon successful manual login, credentials saved for future auto-login

**Server Status Validation:**
- After login, `StatusCheckAsync()` called during MainWindow_Loaded
- Performs GET request to base route to verify server connectivity
- Failure shows "Not connected to server" error and prevents further initialization

#### Code References
```csharp
// File: MainWindow.xaml.cs
private bool AutoLogin(HttpConnectionHandler connection_Handler)
{
    var username = Settings.Default.SavedUsername;
    var password = Settings.Default.SavedPassword;
    
    if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        return false;
    
    bool success = connection_Handler.LoginAsync(username, password);
    if (!success)
    {
        MessageBox.Show("Login Failed.", "Login Error", ...);
        return false;
    }
    return true;
}

// File: Http_Connection_Handler.cs
public bool LoginAsync(string username, string password)
{
    var request = new { Username = username, Password = password };
    var content = new StringContent(JsonSerializer.Serialize(request), ...);
    var response = _httpClient.PostAsync("login", content).GetAwaiter().GetResult();
    
    if (!response.IsSuccessStatusCode)
        return false;
    
    var json = response.Content.ReadAsStringAsync();
    var root = JsonSerializer.Deserialize&lt;JsonObject&gt;(json.Result);
    _token = root["myToken"]?.GetValue&lt;string&gt;();
    
    if (!string.IsNullOrEmpty(_token))
    {
        _httpClient.DefaultRequestHeaders.Add("Authorization", _token);
        return true;
    }
    return false;
}
```

---

### 1.2 Batch Selection System

#### Feature Overview
The batch selection system allows operators to choose from available component batches fetched from the server. The selected batch provides context for all serial number generation requests, ensuring serials are allocated from the correct production batch.

#### Key Components
- **firstComboBox**: WPF ComboBox UI element for batch selection
- **FirstOptions**: List&lt;string&gt; binding for dropdown items
- **BatchList**: ListDto&lt;BatchDto&gt; containing full batch metadata

#### Batch Loading Flow

**1. Server Request:**
```
GET /api/ezcade_client/main/ComponentBatches
Authorization: [token]
```

**2. Response Processing:**
```csharp
// File: MainWindow.xaml.cs (MainWindow_Loaded)
var batchList = connection_handler.GetComponentBatchListAsync();

foreach (var batch in batchList.Items)
{
    FirstOptions.Add(batch);
}

firstComboBox.SelectedIndex = 0; // Default to "Select Batch"
```

**3. Batch Utilization:**
- When hardware query received, selected batch retrieved via Dispatcher
- Batch name included in SerialNumberRequest payload
- Server uses batch context to allocate appropriate serial numbers

#### Selection Change Behavior
Currently, batch selection is simplified (legacy code commented out):
- Originally supported model selection within batch
- Now batch directly used for serial generation
- Future enhancement could restore model filtering

#### Code References
```csharp
// File: Http_Connection_Handler.cs
public ListDto&lt;string&gt; GetComponentBatchListAsync()
{
    var response = _httpClient.GetAsync(_base_route + "ComponentBatches")
                              .GetAwaiter().GetResult();
    
    if (!response.IsSuccessStatusCode)
        return null;
    
    var json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
    var result = JsonSerializer.Deserialize&lt;ListDto&lt;string&gt;&gt;(json, ...);
    
    return result;
}

// File: MainWindow.xaml.cs (Request_recived)
string batch = firstComboBox.Dispatcher.Invoke(() => 
    firstComboBox.SelectedItem?.ToString());
```

---

### 1.3 Serial Number Generation & Query System

#### Feature Overview
The serial number generation system is the core functionality of the application. It parses hardware query strings, translates them into server API requests, and formats responses according to query specifications.

#### Query Types

The system supports three query types based on hardware command format:

**1. PR (Product Only)**
```
Format: PR [MODEL] [CODETYPE] [COUNT]
Example: "PR ABC123 DM 5"

Parsed Components:
- Type: "PR"
- ProductModel: "ABC123"
- CodeType: "DM" (or "SN")
- Count: 5
```

**2. PB (Product + PCB)**
```
Format: PB [PRODUCTMODEL] [PCBMODEL] [CODETYPE] [COUNT]
Example: "PB PROD456 PCB789 SN 3"

Parsed Components:
- Type: "PB"
- ProductModel: "PROD456"
- PcbModel: "PCB789"
- CodeType: "SN"
- Count: 3
```

**3. CO (Custom)**
```
Format: CO [MODEL] [CODETYPE] [COUNT]
Example: "CO CUSTOM$PART DM 2"

Parsed Components:
- Type: "CO"
- ProductModel: "CUSTOM PART" ($ replaced with space)
- CodeType: "DM"
- Count: 2
```

#### Code Types

**SN (Serial Number)**
- Standard alphanumeric serial format
- Returned directly from server's SerialNumber field
- Minimal formatting applied

**DM (Data Matrix)**
- Data matrix barcode format
- Returned from server's DmCode field
- Prefix applied based on query type (PR/PB/CO)

#### Data Flow Pipeline

**Step 1: Hardware Query Reception**
```
Hardware → TCP (127.0.0.1:1000) → Listen() → "PR ABC123 DM 5"
```

**Step 2: Query Parsing**
```csharp
// File: SerialNumberQuery.cs
SerialNumberQuery query = new SerialNumberQuery("PR ABC123 DM 5");

// Internal parsing splits string and extracts:
query.Type = "PR";
query.ProductModel = "ABC123";
query.CodeType = "DM";
query.Count = 5;
```

**Step 3: Request Building**
```csharp
// File: Http_Connection_Handler.cs (GetSerialNumber)
var request = new SerialNumberRequest
{
    BatchName = "SelectedBatch",
    ProductModel = "ABC123",
    Type = "PR",
    PcbModel = null // Only set for PB queries
};
```

**Step 4: Server Communication**
```
POST /api/ezcade_client/main/serialNumber
Content-Type: application/json
Authorization: [token]

{
  "BatchName": "SelectedBatch",
  "ProductModel": "ABC123",
  "Type": "PR",
  "PcbModel": null
}
```

**Step 5: Response Deserialization**
```csharp
// Server Response:
{
  "SerialNumber": "12345ABCD",
  "DmCode": "E4F2G6H8"
}

// Deserialized to:
SerialNumberDto dto = new()
{
    SerialNumber = "12345ABCD",
    DmCode = "E4F2G6H8"
};
```

**Step 6: Formatting**
```csharp
// File: SerialNumberQuery.cs (GetSerialNumber)
public string GetSerialNumber(string serialNumber, string dmCode)
{
    if (Type == "PR")
    {
        if (CodeType == "DM")
            serialNumber = "PR" + serialNumber; // "PRE4F2G6H8"
    }
    else if (Type == "PB")
    {
        if (CodeType == "DM")
            serialNumber = "PB" + serialNumber; // "PBE4F2G6H8"
    }
    else if (Type == "CO")
    {
        if (CodeType == "DM")
            serialNumber = dmCode; // Direct DM code, no prefix
    }
    
    return serialNumber;
}
```

**Step 7: Hardware Transmission**
```
Formatted Serial → SendAndPrint() → TCP Stream → Hardware
```

#### Error Handling in Query System

**Invalid Format:**
- SerialNumberQuery constructor throws FormatException
- Caught in Listen thread, loop continues

**Server Error:**
- GetSerialNumber() returns SerialNumberDto with SerialNumber = "ERROR"
- Condition check prevents sending to hardware: `if (SerialNumberObj.SerialNumber != "ERROR")`

**Network Failure:**
- HTTP request fails, returns empty SerialNumberDto
- Displayed in UI but not sent to hardware

---

### 1.4 Hardware Communication Workflow

#### Feature Overview
The EZcade_Connection_Handler establishes a TCP server listening on localhost for incoming hardware requests. This bidirectional communication channel allows the hardware to query for serial numbers and receive formatted responses.

#### TCP Server Architecture

**Configuration:**
- IP Address: 127.0.0.1 (localhost)
- Port: 1000
- Protocol: TCP with persistent connection
- Buffer Size: 1024 bytes
- Encoding: ASCII

**Initialization Sequence:**
```csharp
// File: EZcade_Connection_Handler.cs
public EZcade_Connection_Handler()
{
    Ezcadeserver = new TcpListener(IPAddress.Parse("127.0.0.1"), 1000);
    Ezcadeserver.Start();
    Ezcadeserver.Server.SetSocketOption(SocketOptionLevel.Socket, 
                                        SocketOptionName.ReuseAddress, true);
}

public void init()
{
    Ezcadeclient = Ezcadeserver.AcceptTcpClient(); // Blocks until connection
    Ezcadestream = Ezcadeclient.GetStream();
}
```

#### Communication Flow

**Listen Phase (Receiving):**
```csharp
public String Listen()
{
    try
    {
        byte[] buffer = new byte[1024];
        int bytesRead = Ezcadestream.Read(buffer, 0, buffer.Length);
        string receivedData = Encoding.ASCII.GetString(buffer, 0, bytesRead);
        return receivedData;
    }
    catch
    {
        return "ERROR";
    }
}
```

**Send Phase (Transmitting):**
```csharp
public void SendAndPrint(String response, bool showMessage = true)
{
    try
    {
        byte[] responseBytes = Encoding.ASCII.GetBytes(response);
        Ezcadestream.Write(responseBytes, 0, responseBytes.Length);
        
        if (showMessage)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                MessageBox.Show("printed " + response);
            });
        }
    }
    catch (Exception e)
    {
        MessageBox.Show(e.Message);
    }
}
```

#### Threading Model

**EZcade_Start_Thread:**
- Created during InitializeThreads()
- Initializes EZcade_Connection_Handler
- Calls init() to accept TCP client connection
- Starts EZcade_Listen_Thread after connection established

**EZcade_Listen_Thread:**
- Runs continuously in background
- Blocks on Listen() waiting for hardware data
- Upon receiving data, calls Request_recived()
- Recreated after each request cycle completes

**Thread Safety:**
- Dispatcher.Invoke() used for UI updates from background threads
- CancellationTokenSource enables graceful shutdown

#### Connection Lifecycle

```
Application Start
    ↓
User Clicks "Connect" Button
    ↓
EZcade_Start_Thread.Start()
    ↓
EZcade_Connection_Handler.init() [Blocks until hardware connects]
    ↓
EZcade_Listen_Thread.Start()
    ↓
[Loop] Listen() → Process Request → SendAndPrint() → Listen() → ...
    ↓
Application Close
    ↓
Cleanup() → Close TcpClient → Stop TcpListener
```

#### Error Handling

**Connection Failures:**
- Exception caught in constructor, MessageBox shown
- Application continues but hardware features unavailable

**Stream Read Errors:**
- Listen() returns "ERROR" string
- Request_recived() filters out ERROR, loop continues

**Stream Write Errors:**
- SendAndPrint() catches exception
- MessageBox displays error message
- Connection may need manual reset

---

### 1.5 Manual vs Automated Processing Modes

#### Feature Overview
The application supports two distinct operational modes controlled by the `IsAutomated` flag and corresponding UI checkbox. These modes dramatically change the user interaction model and workflow automation level.

#### Mode Comparison

| Aspect | Manual Mode | Automated Mode |
|--------|-------------|----------------|
| **User Interaction** | Required for each print | None (fully automated) |
| **Query Processing** | Single query at a time | Batch processing (COUNT queries) |
| **UI Display** | Serial shown in TextBox | No UI display |
| **Button States** | Print/Edit/Cancel enabled | Buttons remain disabled |
| **Serial Editing** | Allowed via Edit button | Not supported |
| **Activation** | Not triggered | Automatic after batch completion |
| **Use Case** | Quality control, verification | High-volume production |

#### Manual Mode Detailed Flow

**Activation:**
```csharp
private void Automation_CheckBox_Unchecked(object sender, RoutedEventArgs e)
{
    IsAutomated = false;
}
```

**Processing Sequence:**

1. **Request Reception:**
   ```
   Hardware sends: "PR ABC123 DM 5"
   ```

2. **Query Parsing & Server Request:**
   ```csharp
   SerialNumberQuery request = new(recived_string);
   SerialNumberObj = connection_handler.GetSerialNumber(request, batch);
   ```

3. **UI Display:**
   ```csharp
   Dispatcher.Invoke(() =>
   {
       serial_number_TextBox.Text = serialNumber_to_print;
       Enable_serialNumber_inteaction(); // Enable buttons
   });
   QueryCount = query.Count - 1; // Store remaining count
   ```

4. **User Actions:**
   - **View**: Operator reviews displayed serial number
   - **Edit** (optional): Click Edit button → TextBox enabled → Modify serial → Click OK
   - **Cancel**: Discard current serial, reset for next request
   - **Print**: Proceed with transmission

5. **Print Execution:**
   ```csharp
   private void print_Buttun_Click(object sender, RoutedEventArgs e)
   {
       Disable_serialNumber_inteaction();
       ezcade_Connection_Handler.SendAndPrint(serial_number_TextBox.Text, showMessage:true);
       
       HandleQueries(RemovePrefix(serial_number_TextBox.Text), SerialNumberObj.DmCode);
       
       serial_number_TextBox.Text = "start requesting in EZcade";
   }
   ```

6. **Subsequent Queries:**
   - If COUNT > 1, HandleQueries() processes remaining requests
   - Each additional query uses same serial number (re-uses SerialNumberObj)
   - Automated loop for remaining queries even in manual mode

#### Automated Mode Detailed Flow

**Activation:**
```csharp
private void Automation_CheckBox_Checked(object sender, RoutedEventArgs e)
{
    IsAutomated = true;
}
```

**Processing Sequence:**

1. **Request Reception & Immediate Processing:**
   ```csharp
   private void HandleQueryResponse(SerialNumberQuery query, string serialNumber, 
                                     string dmCode, int index)
   {
       var serialNumber_to_print = query.GetSerialNumber(serialNumber, dmCode);
       QueryType = query.Type;
       
       if (IsAutomated)
       {
           if (SerialNumberObj.SerialNumber != "ERROR")
               ezcade_Connection_Handler.SendAndPrint(serialNumber_to_print, showMessage: true);
           QueryCount = query.Count - 1;
           HandleQueries(serialNumber, dmCode);
       }
       // ... manual mode handling
   }
   ```

2. **Batch Processing Loop:**
   ```csharp
   private void HandleQueries(string serialNumber, string dmCode)
   {
       // Process remaining queries (COUNT - 1)
       for (int i = 0; i < QueryCount; i++)
       {
           var newRecived_string = ezcade_Connection_Handler.Listen();
           SerialNumberQuery query = new(newRecived_string);
           var serialNumber_to_print = query.GetSerialNumber(serialNumber, dmCode);
           
           if(SerialNumberObj.SerialNumber != "ERROR")
           {
               ezcade_Connection_Handler.SendAndPrint(serialNumber_to_print, showMessage: true);
           }
       }
       
       // Activation after batch complete
       if (QueryType == "PR")
           connection_handler.Activation(serialNumber, "product");
       else if(QueryType == "PB")
           connection_handler.Activation(serialNumber, "pcb");
       
       // Restart listen thread for next batch
       EZcade_Listen_Thread = new Thread(() => { ... });
       EZcade_Listen_Thread.Start();
   }
   ```

3. **Workflow Characteristics:**
   - **No UI Blocking**: Operator free to prepare next batch
   - **High Throughput**: No manual intervention between queries
   - **Automatic Activation**: Product marked active in server system
   - **Batch Integrity**: All queries in COUNT use same serial number
   - **Error Resilience**: ERROR serials filtered, batch continues

#### Example Scenarios

**Manual Mode Example:**
```
Scenario: Operator needs to verify and potentially modify serial

1. Hardware sends: "PR WIDGET DM 3"
2. App retrieves: SerialNumber="12345", DmCode="ABC"
3. Display: "PRABC" in TextBox
4. Operator sees error in serial format
5. Clicks "Edit" → Modifies to "PRXYZ"
6. Clicks "OK" to confirm
7. Clicks "Print" → Sends "PRXYZ" to hardware
8. Remaining 2 queries auto-processed with original "ABC"
```

**Automated Mode Example:**
```
Scenario: Production line running 100 components per batch

1. Hardware sends: "PB BOARD123 PCB456 SN 100"
2. App retrieves: SerialNumber="S00001"
3. IMMEDIATE: Sends "S00001" to hardware (no UI display)
4. Loop: Listen for 99 more queries
5. Each query: Send "S00001" automatically
6. After query 100: POST /activation/pcb/S00001
7. Reset: Listen for next batch query
```

---

### 1.6 Data Matrix Code Handling

#### Feature Overview
Data Matrix (DM) is a 2D barcode format supported as an alternative to standard serial numbers. The system handles both code types through the CodeType field in queries and applies appropriate formatting based on query type.

#### Code Type Selection

**SN (Serial Number):**
- Alphanumeric identifier
- Human-readable format
- Stored in SerialNumber field of response

**DM (Data Matrix):**
- 2D barcode data
- Compact encoding
- Stored in DmCode field of response

#### Formatting Rules by Query Type

**PR (Product) + DM:**
```csharp
// Input Query: "PR WIDGET DM 5"
// Server Response: { SerialNumber: "12345", DmCode: "ABC123XYZ" }
// Formatted Output: "PRABC123XYZ"

if (Type == "PR" &amp;&amp; CodeType == "DM")
    serialNumber = "PR" + serialNumber; // Note: Uses serialNumber field, not dmCode
```

**PB (Product+PCB) + DM:**
```csharp
// Input Query: "PB BOARD123 PCB456 DM 3"
// Server Response: { SerialNumber: "67890", DmCode: "DEF456UVW" }
// Formatted Output: "PBDEF456UVW"

if (Type == "PB" &amp;&amp; CodeType == "DM")
    serialNumber = "PB" + serialNumber;
```

**CO (Custom) + DM:**
```csharp
// Input Query: "CO SPECIAL$PART DM 2"
// Server Response: { SerialNumber: "99999", DmCode: "GHI789RST" }
// Formatted Output: "GHI789RST" (no prefix)

if (Type == "CO" &amp;&amp; CodeType == "DM")
    serialNumber = dmCode; // Uses dmCode directly
```

**SN (Serial Number) Mode:**
```csharp
// Input Query: "PR WIDGET SN 5"
// Server Response: { SerialNumber: "SERIAL12345", DmCode: "..." }
// Formatted Output: "SERIAL12345" (no prefix, direct serial)

// No prefix applied for SN code type regardless of query type
```

#### Implementation Details

**Query Parsing:**
```csharp
// File: SerialNumberQuery.cs
public string CodeType { get; set; } = ""; // "SN" or "DM"

// Parsed from query string position:
// PR: parts[2]
// PB: parts[3]
// CO: parts[2]
```

**Response DTO:**
```csharp
// File: SerialNumberDto.cs
public class SerialNumberDto
{
    public string SerialNumber { get; set; } = string.Empty;
    public string DmCode { get; set; } = string.Empty;
}
```

**Formatting Logic:**
```csharp
// File: SerialNumberQuery.cs
public string GetSerialNumber(string serialNumber, string dmCode)
{
    if (Type == "PR")
    {
        if (CodeType == "DM")
            serialNumber = "PR" + serialNumber;
    }
    else if (Type == "PB")
    {
        if (CodeType == "DM")
            serialNumber = "PB" + serialNumber;
    }
    else if (Type == "CO")
    {
        if (CodeType == "DM")
            serialNumber = dmCode; // Special case: direct DM code
    }
    
    return serialNumber;
}
```

#### UI Checkbox (Legacy)

**Note:** The UI includes a DataMatrix checkbox, but it's currently non-functional in the main flow:

```csharp
private bool IsDataMatrixActive = false;

private void DataMatrix_CheckBox_Checked(object sender, RoutedEventArgs e)
{
    IsDataMatrixActive = true;
}
```

This flag is set but never used in request processing. The CodeType is determined entirely by the hardware query string, not the UI checkbox. This suggests future enhancement potential for operator-controlled code type override.

#### Prefix Removal for Activation

When activating products, the prefix must be removed:

```csharp
// File: MainWindow.xaml.cs
public string RemovePrefix(string input)
{
    if (input.StartsWith("PR"))
        return input.Substring(2); // "PRABC123" → "ABC123"
    if (input.StartsWith("PB"))
        return input.Substring(2); // "PBXYZ789" → "XYZ789"
    return input; // CO type has no prefix
}

// Used in print button click:
HandleQueries(RemovePrefix(serial_number_TextBox.Text), SerialNumberObj.DmCode);
```

---

### 1.7 Product/PCB Activation Workflow

#### Feature Overview
After successful serial number batch processing, the application notifies the server to activate the component. Activation marks the serial as officially in use and typically triggers downstream processes like inventory updates and quality tracking.

#### Activation Trigger Conditions

**When Activation Occurs:**
- Automated mode only (`IsAutomated == true`)
- After all queries in COUNT batch processed
- Only if serial generation was successful (not "ERROR")

**When Activation Does NOT Occur:**
- Manual mode (operator may cancel)
- Single query processing (COUNT == 1)
- Error occurred during serial generation

#### Activation API Endpoint

**Endpoint Format:**
```
POST /api/ezcade_client/main/activation/{fullType}/{serialNumber}
Authorization: [token]
```

**Parameters:**
- **fullType**: "product" or "pcb"
- **serialNumber**: The base serial without prefix

**Examples:**
```
POST /api/ezcade_client/main/activation/product/12345ABCD
POST /api/ezcade_client/main/activation/pcb/67890XYZW
```

#### Type Determination Logic

```csharp
// File: MainWindow.xaml.cs (HandleQueries method)
if (QueryType == "PR")
{
    var response = connection_handler.Activation(serialNumber, "product");
}
else if(QueryType == "PB")
{
    var response = connection_handler.Activation(serialNumber, "pcb");
}
```

**Type Mapping:**
- **PR (Product)** → fullType = "product"
- **PB (Product+PCB)** → fullType = "pcb"
- **CO (Custom)** → No activation (not implemented)

#### Implementation

```csharp
// File: Http_Connection_Handler.cs
public bool Activation(string serialNumber, string fullType)
{
    var url = $"{_base_route}activation/{fullType}/{serialNumber}";
    var response = _httpClient.PostAsync(url, null).GetAwaiter().GetResult();
    
    return response.IsSuccessStatusCode;
}
```

#### Full Activation Workflow

**Step-by-Step Sequence:**

1. **Batch Processing Completion:**
   ```
   Hardware sends COUNT=5 queries
   → App processes all 5 with same serial "SN12345"
   → All queries successful
   ```

2. **Type Extraction:**
   ```csharp
   QueryType = query.Type; // Set during HandleQueryResponse
   // QueryType now "PR" or "PB"
   ```

3. **Serial Preparation:**
   ```csharp
   string cleanSerial = RemovePrefix(serial_number_TextBox.Text);
   // "PRSN12345" → "SN12345"
   // "PBSN67890" → "SN67890"
   ```

4. **Activation Request:**
   ```csharp
   if (QueryType == "PR")
       connection_handler.Activation("SN12345", "product");
   // Result: POST /api/ezcade_client/main/activation/product/SN12345
   ```

5. **Server Processing:**
   - Server receives activation request
   - Updates database: serial marked as active
   - Triggers business logic (inventory, tracking, etc.)
   - Returns success/failure status

6. **Client Continuation:**
   ```csharp
   // Restart listen thread for next batch
   EZcade_Listen_Thread = new Thread(() =>
   {
       try
       {
           string request = ezcade_Connection_Handler.Listen();
           Request_recived(request);
       }
       catch { }
   });
   EZcade_Listen_Thread.Start();
   ```

#### Error Handling

**Current Implementation:**
- Activation return value (bool) captured but not used
- No retry logic on activation failure
- No user notification if activation fails
- Application continues to next batch regardless

**Improvement Opportunities:**
```csharp
// Suggested enhancement:
bool activated = connection_handler.Activation(serialNumber, "product");
if (!activated)
{
    MessageBox.Show($"Warning: Failed to activate {serialNumber}. Please verify manually.",
                    "Activation Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
    // Log to file for audit trail
}
```

#### Business Logic Implications

**Why Activation Matters:**
1. **Inventory Control**: Prevents duplicate serial issuance
2. **Traceability**: Links serial to production batch and timestamp
3. **Quality Assurance**: Enables tracking through manufacturing stages
4. **Compliance**: Regulatory requirements for serialized products
5. **Warranty Management**: Activation timestamp for warranty start date

---

## 2. Logical Flows & Workflows (Detailed Sequences)

### 2.1 Application Startup Flow

#### Complete Initialization Sequence

```
┌─────────────────────────────────────────────────────────────────────┐
│ APPLICATION STARTUP SEQUENCE                                        │
└─────────────────────────────────────────────────────────────────────┘

[1] App.xaml Entry Point
    │
    ├─ Application.OnStartup()
    │   └─ Load global styles from App.xaml resources
    │
    ↓
[2] MainWindow Constructor
    │
    ├─ InitializeComponent() ─────────────────┐
    │   └─ Parse XAML, create UI elements      │
    │                                           │
    ├─ Create HttpConnectionHandler ───────────┤
    │   │                                       │
    │   ├─ BaseAddress = "http://192.168.1.122:8080/"
    │   └─ Initialize HttpClient               │
    │                                           │
    ├─ Attempt AutoLogin() ────────────────────┤
    │   │                                       │
    │   ├─ Read Settings.Default.SavedUsername │
    │   ├─ Read Settings.Default.SavedPassword │
    │   │                                       │
    │   ├─ IF (credentials exist)              │
    │   │   ├─ POST /login                     │
    │   │   ├─ Receive token                   │
    │   │   └─ Add to HttpClient headers       │
    │   │                                       │
    │   ├─ IF (login succeeds)                 │
    │   │   └─ Return true                     │
    │   │                                       │
    │   └─ IF (login fails OR no credentials)  │
    │       ├─ Return false                    │
    │       └─ Show LoginWindow (MODAL BLOCK) ─┤
    │           │                               │
    │           ├─ User enters credentials      │
    │           ├─ POST /login                  │
    │           ├─ Save credentials to Settings │
    │           └─ Close dialog                 │
    │                                           │
    ├─ Register MainWindow_Loaded event ───────┤
    │                                           │
    └─ Set DataContext = this ─────────────────┘
    │
    ↓
[3] MainWindow.Loaded Event
    │
    ├─ StatusCheckAsync() ─────────────────────┐
    │   │                                       │
    │   ├─ GET /api/ezcade_client/main/        │
    │   ├─ IF (fails)                           │
    │   │   ├─ Show "Not connected to server"  │
    │   │   └─ STOP (return)                   │
    │   └─ IF (success) continue                │
    │                                           │
    ├─ Show_ip() ──────────────────────────────┤
    │   └─ Display "connected through 192.168.1.122:8080"
    │                                           │
    ├─ Init_Form() ────────────────────────────┤
    │   │                                       │
    │   ├─ InitializeThreads() ─────────────┐  │
    │   │   │                               │  │
    │   │   ├─ Create EZcade_Listen_Thread ─┤  │
    │   │   │   └─ Action: Listen() → Request_recived()
    │   │   │                               │  │
    │   │   └─ Create EZcade_Start_Thread ──┤  │
    │   │       └─ Action: EZcade_Connection_Handler.init()
    │   │                                   │  │
    │   └─ InitializeUI() ──────────────────┤  │
    │       ├─ Edit_Button.Content = "Connect"
    │       ├─ serial_TextBox.Text = "press connect..."
    │       ├─ print_Button.Visibility = Hidden
    │       └─ Cancel_Button.Visibility = Hidden
    │                                           │
    ├─ GetComponentBatchListAsync() ───────────┤
    │   │                                       │
    │   ├─ GET /api/ezcade_client/main/ComponentBatches
    │   └─ Receive ListDto&lt;string&gt;              │
    │                                           │
    ├─ Populate Batch Dropdown ────────────────┤
    │   │                                       │
    │   ├─ foreach batch in batchList.Items    │
    │   │   └─ FirstOptions.Add(batch)         │
    │   │                                       │
    │   └─ firstComboBox.SelectedIndex = 0     │
    │       └─ Shows "Select Batch"            │
    │                                           │
    └───────────────────────────────────────────┘
    │
    ↓
[4] Application Ready State
    │
    ├─ UI Displayed
    ├─ Batch dropdown populated
    ├─ Buttons in initial state
    └─ Waiting for user to click "Connect"
```

#### State Transitions During Startup

```
STATE: Application Launch
    ├─ UI: None (not visible)
    ├─ Connection: Not established
    └─ Threads: None
    
    ↓ [Constructor completes]
    
STATE: Login Phase
    ├─ UI: LoginWindow (if auto-login fails)
    ├─ Connection: HTTP handshake in progress
    └─ Threads: None
    
    ↓ [Login succeeds]
    
STATE: Initialization Phase
    ├─ UI: MainWindow visible, disabled state
    ├─ Connection: HTTP authenticated, TCP not started
    └─ Threads: Created but not started
    
    ↓ [MainWindow_Loaded completes]
    
STATE: Ready for Connection
    ├─ UI: "Connect" button enabled
    ├─ Connection: HTTP active, TCP inactive
    └─ Threads: Dormant, waiting for Connect button
```

#### Error Scenarios

**Scenario 1: Server Unreachable at Startup**
```
AutoLogin() → HTTP request timeout
    ↓
LoginWindow shown
    ↓
User enters credentials → HTTP request timeout
    ↓
MessageBox: "Login Error: [exception]"
    ↓
LoginWindow remains open (retry loop)
```

**Scenario 2: Status Check Fails**
```
MainWindow_Loaded() → StatusCheckAsync() returns false
    ↓
MessageBox: "Not connected to server"
    ↓
return; (early exit, no initialization)
    ↓
Application visible but non-functional
```

**Scenario 3: Batch List Unavailable**
```
GetComponentBatchListAsync() → HTTP 500 or timeout
    ↓
Returns null
    ↓
foreach loop handles null gracefully (no items added)
    ↓
Dropdown shows only "Select Batch"
    ↓
Application functional but limited
```

---

### 2.2 Hardware Request Processing Flow (Manual Mode)

#### Complete Manual Request Lifecycle

```
┌─────────────────────────────────────────────────────────────────────┐
│ MANUAL MODE REQUEST PROCESSING (IsAutomated = false)               │
└─────────────────────────────────────────────────────────────────────┘

[PRECONDITION: User has clicked Connect, TCP connection established]

┌─────────────────────┐
│ EZcade_Listen_Thread│ (Background thread, blocking)
└─────────────────────┘
    │
    ├─ Listen() ──────────────────────────────────────┐
    │   │                                              │
    │   ├─ byte[] buffer = new byte[1024]             │
    │   ├─ int bytesRead = Ezcadestream.Read(...)     │
    │   │   └─ [BLOCKS HERE until data arrives]       │
    │   │                                              │
    │   └─ return Encoding.ASCII.GetString(buffer)    │
    │                                                  │
    ↓                                                  │
┌───────────────────────────────────────────────────────┐
│ HARDWARE SENDS REQUEST                               │
│ Example: "pr abc123 dm 5"                            │
│ (lowercase from hardware)                            │
└───────────────────────────────────────────────────────┘
    │
    ├─ receivedData = "pr abc123 dm 5"               │
    │                                                  │
    ↓                                                  │
Request_recived(recived_string)                        │
    │                                                  │
    ├─ recived_string.ToUpper() ─────────────────────┤
    │   └─ "PR ABC123 DM 5"                           │
    │                                                  │
    ├─ Get selected batch from UI (Dispatcher) ──────┤
    │   string batch = firstComboBox.Dispatcher.Invoke(
    │       () =&gt; firstComboBox.SelectedItem?.ToString());
    │   └─ batch = "BATCH_2024_001"                   │
    │                                                  │
    ├─ IF (recived_string != "" &amp;&amp; != "ERROR") ─────┤
    │   │                                              │
    │   ├─ Parse Query ──────────────────────────────┤
    │   │   │                                          │
    │   │   └─ SerialNumberQuery request = new(recived_string)
    │   │       │                                      │
    │   │       ├─ Type = "PR"                         │
    │   │       ├─ ProductModel = "ABC123"             │
    │   │       ├─ CodeType = "DM"                     │
    │   │       └─ Count = 5                           │
    │   │                                              │
    │   ├─ Request Serial from Server ───────────────┤
    │   │   │                                          │
    │   │   └─ SerialNumberObj = connection_handler.GetSerialNumber(request, batch)
    │   │       │                                      │
    │   │       ├─ Build SerialNumberRequest DTO      │
    │   │       │   ├─ BatchName = "BATCH_2024_001"   │
    │   │       │   ├─ ProductModel = "ABC123"        │
    │   │       │   ├─ Type = "PR"                    │
    │   │       │   └─ PcbModel = null                │
    │   │       │                                      │
    │   │       ├─ POST /api/ezcade_client/main/serialNumber
    │   │       │                                      │
    │   │       ├─ Receive Response ──────────────────┤
    │   │       │   {                                  │
    │   │       │     "serialNumber": "SN00001",      │
    │   │       │     "dmCode": "E4F2G6H8"            │
    │   │       │   }                                  │
    │   │       │                                      │
    │   │       └─ Deserialize to SerialNumberDto     │
    │   │           └─ SerialNumberObj.SerialNumber = "SN00001"
    │   │           └─ SerialNumberObj.DmCode = "E4F2G6H8"
    │   │                                              │
    │   └─ HandleQueryResponse(request, serialNumber, dmCode, 1)
    │                                                  │
    ↓                                                  │
HandleQueryResponse(query, "SN00001", "E4F2G6H8", 1)  │
    │                                                  │
    ├─ Format Serial ─────────────────────────────────┤
    │   │                                              │
    │   └─ serialNumber_to_print = query.GetSerialNumber("SN00001", "E4F2G6H8")
    │       │                                          │
    │       ├─ Type == "PR" &amp;&amp; CodeType == "DM"      │
    │       └─ return "PR" + serialNumber             │
    │           └─ "PRSN00001"                         │
    │                                                  │
    ├─ QueryType = query.Type ───────────────────────┤
    │   └─ QueryType = "PR"                           │
    │                                                  │
    ├─ IF (IsAutomated == false) [MANUAL MODE] ──────┤
    │   │                                              │
    │   ├─ Dispatcher.Invoke(() =&gt; {...}) ────────────┤
    │   │   │                                          │
    │   │   ├─ serial_number_TextBox.Text = "PRSN00001"
    │   │   │                                          │
    │   │   └─ Enable_serialNumber_inteaction() ──────┤
    │   │       ├─ print_Buttun.IsEnabled = true      │
    │   │       ├─ Edit_Button.IsEnabled = true       │
    │   │       └─ Cancel_Button.IsEnabled = true     │
    │   │                                              │
    │   └─ QueryCount = query.Count - 1 ──────────────┤
    │       └─ QueryCount = 4                          │
    │                                                  │
    ↓                                                  │
┌────────────────────────────────────────────────────────┐
│ UI STATE: Waiting for Operator Action                │
│                                                        │
│ ┌───────────────────────────────────────────┐         │
│ │ Serial Number: PRSN00001                  │         │
│ ├───────────────────────────────────────────┤         │
│ │ [Edit] [Print] [Cancel]  <-- All enabled  │         │
│ └───────────────────────────────────────────┘         │
└────────────────────────────────────────────────────────┘
    │
    ├─ OPERATOR ACTION: [Print] ─────────────────────────┐
    │                                                      │
    ↓                                                      │
print_Buttun_Click(sender, e)                             │
    │                                                      │
    ├─ Disable_serialNumber_inteaction() ─────────────────┤
    │   └─ All buttons disabled                           │
    │                                                      │
    ├─ Send to Hardware ──────────────────────────────────┤
    │   │                                                  │
    │   └─ ezcade_Connection_Handler.SendAndPrint("PRSN00001", showMessage:true)
    │       │                                              │
    │       ├─ Convert to ASCII bytes                     │
    │       ├─ Ezcadestream.Write(responseBytes)          │
    │       └─ MessageBox.Show("printed PRSN00001")       │
    │                                                      │
    ├─ Process Remaining Queries ─────────────────────────┤
    │   │                                                  │
    │   └─ HandleQueries(RemovePrefix("PRSN00001"), "E4F2G6H8")
    │       │                      └─ "SN00001"           │
    │       │                                              │
    │       ├─ for (int i = 0; i &lt; QueryCount; i++) [4 iterations]
    │       │   │                                          │
    │       │   ├─ Listen() ───────────────────────────┐  │
    │       │   │   └─ [BLOCKS until hardware sends]   │  │
    │       │   │       └─ Hardware sends next query   │  │
    │       │   │           (one of remaining 4)       │  │
    │       │   │                                      │  │
    │       │   ├─ Parse query                         │  │
    │       │   ├─ GetSerialNumber(same SN, same DM)  │  │
    │       │   │   └─ "PRSN00001" (reused)           │  │
    │       │   │                                      │  │
    │       │   └─ SendAndPrint("PRSN00001")          │  │
    │       │       └─ MessageBox each time            │  │
    │       │                                          │  │
    │       └─ [Loop completes after 4 queries]       │  │
    │                                                  │  │
    ├─ Reset UI ─────────────────────────────────────────┤
    │   └─ serial_number_TextBox.Text = "start requesting in EZcade"
    │                                                      │
    └──────────────────────────────────────────────────────┘
    │
    ↓
[Listen thread terminated, QueryCount completed]
[Operator must click Cancel or wait for new connection cycle]
```

#### Alternate Operator Actions

**OPERATOR ACTION: [Edit]**
```
Edit_Button_Click()
    │
    ├─ IF (Content == "Edit")
    │   ├─ serial_number_TextBox.IsEnabled = true
    │   ├─ Cancel_Button.IsEnabled = false
    │   ├─ print_Buttun.IsEnabled = false
    │   └─ Edit_Button.Content = "OK"
    │
    ↓
[Operator types in TextBox]
    │
    ├─ Modify: "PRSN00001" → "PRCUSTOM123"
    │
    ↓
Edit_Button_Click() [Second click]
    │
    ├─ IF (Content == "OK")
    │   ├─ serial_number_TextBox.IsEnabled = false
    │   ├─ Cancel_Button.IsEnabled = true
    │   ├─ print_Buttun.IsEnabled = true
    │   └─ Edit_Button.Content = "Edit"
    │
    ↓
[Print button now sends modified value "PRCUSTOM123"]
```

**OPERATOR ACTION: [Cancel]**
```
Cancel_Button_Click()
    │
    ├─ Disable_serialNumber_inteaction()
    │   └─ All buttons disabled
    │
    ├─ serial_number_TextBox.Text = "start requesting in EZcade"
    │
    ├─ Recreate Listen Thread ────────────────────┐
    │   │                                          │
    │   └─ EZcade_Listen_Thread = new Thread(...) │
    │       └─ Listen() → Request_recived()       │
    │                                              │
    └─ EZcade_Listen_Thread.Start() ──────────────┘
        └─ Ready for new hardware request
```

---

### 2.3 Automated Query Batch Processing Flow

#### Complete Automated Workflow

```
┌─────────────────────────────────────────────────────────────────────┐
│ AUTOMATED MODE REQUEST PROCESSING (IsAutomated = true)             │
└─────────────────────────────────────────────────────────────────────┘

[PRECONDITION: Automation checkbox checked, TCP connected]

┌────────────────────────────────────────────────────────────────┐
│ HARDWARE SENDS BATCH REQUEST                                   │
│ Example: "PB PRODUCT_MODEL PCB_MODEL SN 100"                   │
│ (Requests 100 serial numbers)                                  │
└────────────────────────────────────────────────────────────────┘
    │
    ↓
EZcade_Listen_Thread
    ├─ Listen() receives "PB PRODUCT_MODEL PCB_MODEL SN 100"
    └─ Request_recived("pb product_model pcb_model sn 100")
        │
        ├─ ToUpper() → "PB PRODUCT_MODEL PCB_MODEL SN 100"
        ├─ Get batch → "BATCH_2024_Q1"
        ├─ Parse SerialNumberQuery
        │   ├─ Type = "PB"
        │   ├─ ProductModel = "PRODUCT_MODEL"
        │   ├─ PcbModel = "PCB_MODEL"
        │   ├─ CodeType = "SN"
        │   └─ Count = 100
        │
        └─ GetSerialNumber(query, batch)
            │
            ├─ POST /api/ezcade_client/main/serialNumber
            │   {
            │     "batchName": "BATCH_2024_Q1",
            │     "productModel": "PRODUCT_MODEL",
            │     "type": "PB",
            │     "pcbModel": "PCB_MODEL"
            │   }
            │
            └─ Receive Response
                {
                  "serialNumber": "PCB00001",
                  "dmCode": "XYZ123ABC"
                }
                └─ SerialNumberObj.SerialNumber = "PCB00001"
    │
    ↓
HandleQueryResponse(query, "PCB00001", "XYZ123ABC", 1)
    │
    ├─ Format Serial
    │   └─ serialNumber_to_print = query.GetSerialNumber("PCB00001", "XYZ123ABC")
    │       └─ Type == "PB" &amp;&amp; CodeType == "SN"
    │       └─ return "PCB00001" (no prefix for SN type)
    │
    ├─ QueryType = "PB"
    │
    ├─ IF (IsAutomated == true) [AUTOMATED MODE]
    │   │
    │   ├─ IF (SerialNumberObj.SerialNumber != "ERROR")
    │   │   └─ ezcade_Connection_Handler.SendAndPrint("PCB00001", showMessage: true)
    │   │       ├─ Write to TCP stream
    │   │       └─ MessageBox: "printed PCB00001" [Query 1 of 100]
    │   │
    │   ├─ QueryCount = query.Count - 1
    │   │   └─ QueryCount = 99
    │   │
    │   └─ HandleQueries("PCB00001", "XYZ123ABC")
    │
    ↓
HandleQueries("PCB00001", "XYZ123ABC") [BATCH PROCESSING LOOP]
    │
    ├─ for (int i = 0; i &lt; QueryCount; i++) [99 iterations]
    │   │
    │   ├─────────────────────────────────────────────────────┐
    │   │ ITERATION 1 (Query 2 of 100)                        │
    │   ├─────────────────────────────────────────────────────┤
    │   │                                                      │
    │   ├─ var newRecived_string = ezcade_Connection_Handler.Listen()
    │   │   └─ [BLOCKS until hardware sends next query]      │
    │   │   └─ Hardware sends: "PB PRODUCT_MODEL PCB_MODEL SN 1"
    │   │       (Note: Hardware sends COUNT=1 for remaining)  │
    │   │                                                      │
    │   ├─ SerialNumberQuery query = new(newRecived_string)   │
    │   │   ├─ Type = "PB"                                    │
    │   │   ├─ ProductModel = "PRODUCT_MODEL"                 │
    │   │   ├─ PcbModel = "PCB_MODEL"                         │
    │   │   ├─ CodeType = "SN"                                │
    │   │   └─ Count = 1                                      │
    │   │                                                      │
    │   ├─ var serialNumber_to_print = query.GetSerialNumber("PCB00001", "XYZ123ABC")
    │   │   └─ Uses SAME serial from initial request          │
    │   │   └─ "PCB00001" (reused for all 100 queries)       │
    │   │                                                      │
    │   ├─ IF (SerialNumberObj.SerialNumber != "ERROR")      │
    │   │   └─ ezcade_Connection_Handler.SendAndPrint("PCB00001", showMessage: true)
    │   │       └─ MessageBox: "printed PCB00001" [Query 2]  │
    │   │                                                      │
    │   ├─────────────────────────────────────────────────────┤
    │   │ ITERATION 2-99 (Queries 3-100)                      │
    │   ├─────────────────────────────────────────────────────┤
    │   │                                                      │
    │   └─ [Repeat above process for remaining 98 queries]    │
    │       └─ Each: Listen() → Parse → Format → SendAndPrint()
    │           └─ All use same "PCB00001" serial             │
    │                                                          │
    └─ [Loop completes after 99 iterations]                   │
        └─ Total: 1 initial + 99 loop = 100 queries processed
    │
    ↓
ACTIVATION PHASE
    │
    ├─ Determine activation type based on QueryType
    │   │
    │   ├─ IF (QueryType == "PR")
    │   │   └─ connection_handler.Activation("PCB00001", "product")
    │   │
    │   └─ IF (QueryType == "PB") [This case]
    │       └─ connection_handler.Activation("PCB00001", "pcb")
    │           │
    │           ├─ POST /api/ezcade_client/main/activation/pcb/PCB00001
    │           │
    │           └─ Server Response
    │               ├─ Update database: PCB00001 marked active
    │               ├─ Trigger downstream processes
    │               └─ Return success (200 OK)
    │
    ↓
THREAD RESTART
    │
    ├─ Create new EZcade_Listen_Thread ──────────────────────┐
    │   │                                                     │
    │   └─ new Thread(() =&gt; {                                │
    │         try {                                           │
    │             string request = ezcade_Connection_Handler.Listen();
    │             Request_recived(request);                   │
    │         }                                               │
    │         catch { }                                       │
    │       })                                                │
    │                                                         │
    └─ EZcade_Listen_Thread.Start() ─────────────────────────┘
        └─ Ready for next batch request
    │
    ↓
┌────────────────────────────────────────────────────────────────┐
│ BATCH COMPLETE                                                 │
│ - 100 queries processed                                        │
│ - Serial "PCB00001" sent 100 times                            │
│ - Activation endpoint called                                   │
│ - System ready for next batch                                 │
└────────────────────────────────────────────────────────────────┘
```

#### Timing and Performance

**Example Timing Analysis:**
```
Batch Size: 100 queries
Network Latency: 50ms per query (Listen + Send)
Server API Time: 200ms (first query only)
Activation Time: 100ms

Total Time Calculation:
├─ First Query: 200ms (server) + 50ms (network) = 250ms
├─ Remaining 99: 99 × 50ms = 4,950ms
├─ Activation: 100ms
└─ Total: 250 + 4,950 + 100 = 5,300ms = 5.3 seconds

Throughput: 100 queries / 5.3s ≈ 18.9 queries/second
```

#### Key Differences from Manual Mode

| Aspect | Manual Mode | Automated Mode |
|--------|-------------|----------------|
| First Query | Display in UI | Immediate send |
| User Wait | Yes, for Print button | No user interaction |
| UI Updates | TextBox updated | No UI updates |
| Message Boxes | One per query | One per query (optional) |
| Activation | Not triggered | Triggered after batch |
| QueryCount | Stored but limited use | Critical for loop control |
| Thread Restart | Manual (Cancel button) | Automatic after batch |

#### Error Handling in Automated Mode

**Scenario: Server Returns ERROR**
```
GetSerialNumber() returns SerialNumberDto { SerialNumber = "ERROR" }
    ↓
HandleQueryResponse()
    ├─ IF (SerialNumberObj.SerialNumber != "ERROR")
    │   └─ SendAndPrint() [SKIPPED]
    │
    └─ HandleQueries() still called
        ├─ Loop: Listen() 99 times
        ├─ Each check: IF (SerialNumberObj.SerialNumber != "ERROR")
        │   └─ SendAndPrint() [SKIPPED for all]
        │
        └─ Activation NOT skipped (potential issue)
            └─ POST /activation/pcb/ERROR
                └─ Server may reject or handle incorrectly
```

**Improvement Opportunity:**
```csharp
// Suggested enhancement:
if (SerialNumberObj.SerialNumber != "ERROR")
{
    // Process batch
    HandleQueries(serialNumber, dmCode);
}
else
{
    MessageBox.Show("Serial generation failed. Batch aborted.");
    RestartListenThread();
}
```

---

### 2.4 Data Transformation Pipeline

#### Complete Data Journey

```
┌────────────────────────────────────────────────────────────────────┐
│ DATA TRANSFORMATION PIPELINE - END TO END                          │
└────────────────────────────────────────────────────────────────────┘

STAGE 1: HARDWARE QUERY (Raw Input)
═══════════════════════════════════════════════════════════════════
Source: EZcade Hardware System
Protocol: TCP over 127.0.0.1:1000
Format: ASCII string, variable length

Example Input: "pr widget$pro dm 5"
                ↑  ↑         ↑  ↑
                │  │         │  └─ Query count
                │  │         └──── Code type (DM/SN)
                │  └────────────── Product model ($ = space placeholder)
                └───────────────── Query type (PR/PB/CO)

Raw Bytes: [112, 114, 32, 119, 105, 100, 103, 101, 116, 36, ...]
ASCII Decode: "pr widget$pro dm 5"


STAGE 2: NORMALIZATION (Case Conversion)
═══════════════════════════════════════════════════════════════════
Location: Request_recived() method

Input:  "pr widget$pro dm 5"
        ↓ .ToUpper()
Output: "PR WIDGET$PRO DM 5"

Purpose: Ensure consistent parsing regardless of hardware case


STAGE 3: QUERY PARSING (String → Object)
═══════════════════════════════════════════════════════════════════
Location: SerialNumberQuery constructor
Algorithm: Split by space, extract components

Input String: "PR WIDGET$PRO DM 5"
               ↓ Split(' ')
String Array: ["PR", "WIDGET$PRO", "DM", "5"]
               [0]   [1]           [2]  [3]

Parsing Logic:
┌─────────────────────────────────────────────────────────────────┐
│ Type Detection: parts[0]                                         │
├─────────────────────────────────────────────────────────────────┤
│                                                                  │
│ IF Type == "PR":                                                 │
│     ProductModel = parts[1]                                      │
│     CodeType = parts[2]                                          │
│     Count = int.Parse(parts[3])                                  │
│                                                                  │
│ IF Type == "PB":                                                 │
│     ProductModel = parts[1]                                      │
│     PcbModel = parts[2]                                          │
│     CodeType = parts[3]                                          │
│     Count = int.Parse(parts[4])                                  │
│                                                                  │
│ IF Type == "CO":                                                 │
│     ProductModel = parts[1].Replace("$", " ")  ← Special handling│
│     CodeType = parts[2]                                          │
│     Count = int.Parse(parts[3])                                  │
│                                                                  │
└─────────────────────────────────────────────────────────────────┘

Parsed Object (SerialNumberQuery):
{
    Type: "PR",
    ProductModel: "WIDGET$PRO",  ← $ not yet replaced (bug?)
    PcbModel: null,
    CodeType: "DM",
    Count: 5
}


STAGE 4: DTO CONSTRUCTION (Query → Request)
═══════════════════════════════════════════════════════════════════
Location: Http_Connection_Handler.GetSerialNumber()
Purpose: Prepare server API payload

SerialNumberQuery → SerialNumberRequest Mapping:
┌──────────────────────────────┬──────────────────────────────┐
│ Source (Query)               │ Destination (Request)        │
├──────────────────────────────┼──────────────────────────────┤
│ [Selected from UI]           │ BatchName                    │
│ query.ProductModel           │ ProductModel                 │
│ query.Type                   │ Type                         │
│ query.PcbModel (if exists)   │ PcbModel                     │
└──────────────────────────────┴──────────────────────────────┘

Request DTO:
{
    "batchName": "BATCH_2024_001",
    "productModel": "WIDGET$PRO",
    "type": "PR",
    "pcbModel": null
}


STAGE 5: SERIALIZATION (Object → JSON)
═══════════════════════════════════════════════════════════════════
Location: Http_Connection_Handler.GetSerialNumber()
Serializer: System.Text.Json

C# Object:
var request = new SerialNumberRequest {
    BatchName = "BATCH_2024_001",
    ProductModel = "WIDGET$PRO",
    Type = "PR",
    PcbModel = null
};

     ↓ JsonSerializer.Serialize(request)

JSON Payload:
{
  "batchName": "BATCH_2024_001",
  "productModel": "WIDGET$PRO",
  "type": "PR",
  "pcbModel": null
}


STAGE 6: HTTP TRANSMISSION (Client → Server)
═══════════════════════════════════════════════════════════════════
Protocol: HTTP POST
Endpoint: http://192.168.1.122:8080/api/ezcade_client/main/serialNumber
Headers:
    Content-Type: application/json
    Authorization: [token_value]

HTTP Request:
POST /api/ezcade_client/main/serialNumber HTTP/1.1
Host: 192.168.1.122:8080
Content-Type: application/json
Authorization: eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...
Content-Length: 102

{
  "batchName": "BATCH_2024_001",
  "productModel": "WIDGET$PRO",
  "type": "PR",
  "pcbModel": null
}


STAGE 7: SERVER PROCESSING (Backend Logic)
═══════════════════════════════════════════════════════════════════
[Server-Side - Not Client Code]

Server Actions:
1. Validate token (Authorization header)
2. Look up batch "BATCH_2024_001" in database
3. Find product model "WIDGET$PRO" within batch
4. Generate or retrieve next available serial number
5. Generate corresponding Data Matrix code
6. Mark serial as "issued" (not yet activated)
7. Return response DTO


STAGE 8: HTTP RESPONSE (Server → Client)
═══════════════════════════════════════════════════════════════════
HTTP Response:
HTTP/1.1 200 OK
Content-Type: application/json
Content-Length: 65

{
  "serialNumber": "WGT00042",
  "dmCode": "D4G7K2M9P1"
}


STAGE 9: DESERIALIZATION (JSON → Object)
═══════════════════════════════════════════════════════════════════
Location: Http_Connection_Handler.GetSerialNumber()

JSON String:
var resultJson = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
// resultJson = "{\"serialNumber\":\"WGT00042\",\"dmCode\":\"D4G7K2M9P1\"}"

     ↓ JsonSerializer.Deserialize&lt;SerialNumberDto&gt;(resultJson)

C# Object:
SerialNumberDto dto = new() {
    SerialNumber = "WGT00042",
    DmCode = "D4G7K2M9P1"
};


STAGE 10: FORMATTING (Apply Prefixes)
═══════════════════════════════════════════════════════════════════
Location: SerialNumberQuery.GetSerialNumber()
Input: SerialNumber="WGT00042", DmCode="D4G7K2M9P1"

Formatting Rules:
┌─────────────────────────────────────────────────────────────────┐
│ public string GetSerialNumber(string serialNumber, string dmCode)│
│ {                                                                 │
│     if (Type == "PR")                                             │
│     {                                                             │
│         if (CodeType == "DM")                                     │
│             serialNumber = "PR" + serialNumber;                   │
│         // Result: "PRWGT00042"                                   │
│     }                                                             │
│     else if (Type == "PB")                                        │
│     {                                                             │
│         if (CodeType == "DM")                                     │
│             serialNumber = "PB" + serialNumber;                   │
│     }                                                             │
│     else if (Type == "CO")                                        │
│     {                                                             │
│         if (CodeType == "DM")                                     │
│             serialNumber = dmCode;  // Use DmCode directly        │
│         // Result: "D4G7K2M9P1" (no prefix)                      │
│     }                                                             │
│     return serialNumber;                                          │
│ }                                                                 │
└─────────────────────────────────────────────────────────────────┘

For our example (Type="PR", CodeType="DM"):
Input:  serialNumber="WGT00042"
Output: "PRWGT00042"


STAGE 11: ASCII ENCODING (String → Bytes)
═══════════════════════════════════════════════════════════════════
Location: EZcade_Connection_Handler.SendAndPrint()

String: "PRWGT00042"
         ↓ Encoding.ASCII.GetBytes()
Bytes: [80, 82, 87, 71, 84, 48, 48, 48, 52, 50]
        P   R   W   G   T   0   0   0   4   2


STAGE 12: TCP TRANSMISSION (Client → Hardware)
═══════════════════════════════════════════════════════════════════
Protocol: TCP
Destination: 127.0.0.1:1000
Method: NetworkStream.Write()

Transmission:
byte[] responseBytes = [80, 82, 87, 71, 84, 48, 48, 48, 52, 50];
Ezcadestream.Write(responseBytes, 0, responseBytes.Length);

Hardware Receives: "PRWGT00042"


COMPLETE TRANSFORMATION SUMMARY
═══════════════════════════════════════════════════════════════════
┌─────────────────────────────────────────────────────────────────┐
│ INPUT  (Hardware): "pr widget$pro dm 5"                          │
│           ↓                                                       │
│ NORMALIZED: "PR WIDGET$PRO DM 5"                                 │
│           ↓                                                       │
│ PARSED: {Type:"PR", Model:"WIDGET$PRO", CodeType:"DM", Count:5}  │
│           ↓                                                       │
│ REQUEST: {BatchName:"BATCH_2024_001", ProductModel:"WIDGET$PRO"} │
│           ↓                                                       │
│ JSON: {"batchName":"BATCH_2024_001","productModel":"WIDGET$PRO"} │
│           ↓                                                       │
│ [HTTP POST to Server]                                             │
│           ↓                                                       │
│ RESPONSE: {"serialNumber":"WGT00042","dmCode":"D4G7K2M9P1"}      │
│           ↓                                                       │
│ DTO: {SerialNumber:"WGT00042", DmCode:"D4G7K2M9P1"}              │
│           ↓                                                       │
│ FORMATTED: "PRWGT00042"                                           │
│           ↓                                                       │
│ OUTPUT (Hardware): "PRWGT00042"                                   │
└─────────────────────────────────────────────────────────────────┘
```

#### Data Flow Diagram (Visual)

```
┌──────────┐       ┌──────────┐       ┌──────────┐       ┌──────────┐
│ Hardware │──TCP──>│  Client  │──HTTP─>│  Server  │──HTTP─>│ Database │
│ (EZcade) │       │   App    │       │   API    │       │          │
└──────────┘       └──────────┘       └──────────┘       └──────────┘
     │                  │                  │                  │
     │ "pr abc123 dm 5" │                  │                  │
     ├─────────────────>│                  │                  │
     │                  │ Parse Query      │                  │
     │                  ├─────────┐        │                  │
     │                  │         │        │                  │
     │                  │<────────┘        │                  │
     │                  │ POST /serialNumber│                 │
     │                  ├──────────────────>│                  │
     │                  │                  │ Lookup Batch     │
     │                  │                  ├─────────────────>│
     │                  │                  │ Get Serial       │
     │                  │                  │<─────────────────┤
     │                  │ SerialNumberDto  │                  │
     │                  │<─────────────────┤                  │
     │                  │ Format Serial    │                  │
     │                  ├─────────┐        │                  │
     │                  │         │        │                  │
     │                  │<────────┘        │                  │
     │  "PRABC123"      │                  │                  │
     │<─────────────────┤                  │                  │
     │                  │                  │                  │
```

---

### 2.5 Error & Exception Flows

#### Comprehensive Error Handling Map

```
┌────────────────────────────────────────────────────────────────────┐
│ ERROR SCENARIOS & EXCEPTION FLOWS                                  │
└────────────────────────────────────────────────────────────────────┘

ERROR CATEGORY 1: NETWORK & CONNECTIVITY
═══════════════════════════════════════════════════════════════════

┌─────────────────────────────────────────────────────────────┐
│ 1.1 Server Unreachable (HTTP Connection Failure)            │
└─────────────────────────────────────────────────────────────┘

Trigger: Server down, network disconnected, wrong IP/port
Location: HttpConnectionHandler.LoginAsync()

Flow:
LoginAsync(username, password)
    ├─ _httpClient.PostAsync("login", content).GetAwaiter().GetResult()
    │   └─ Throws: HttpRequestException
    │       Message: "No connection could be made because the target machine actively refused it"
    │
    └─ Caught in MainWindow.AutoLogin()
        ├─ catch (Exception ex)
        │   └─ MessageBox.Show("Login Error: " + ex.Message)
        │
        ├─ LoginWindow shown (modal)
        │   └─ User can retry
        │
        └─ IF retry fails repeatedly:
            └─ Application stuck in login loop
                └─ User must close application

Consequence: Cannot proceed past login, application non-functional

Improvement Needed:
- Retry mechanism with exponential backoff
- Offline mode or cached data
- Server status indicator in UI


┌─────────────────────────────────────────────────────────────┐
│ 1.2 TCP Connection Lost (Hardware Disconnect)               │
└─────────────────────────────────────────────────────────────┘

Trigger: EZcade hardware disconnects, network cable unplugged
Location: EZcade_Connection_Handler.Listen()

Flow:
Listen()
    ├─ Ezcadestream.Read(buffer, 0, buffer.Length)
    │   └─ Throws: IOException
    │       Message: "Unable to read data from the transport connection"
    │
    └─ catch (generic exception)
        └─ return "ERROR";

Request_recived("ERROR")
    ├─ IF (recived_string != "ERROR")
    │   └─ [Condition FALSE, skip processing]
    │
    └─ Thread terminates silently

Consequence: Listen thread dies, no further requests processed

Current Behavior: Silent failure, operator unaware
Improvement Needed:
- MessageBox notification of connection loss
- Automatic reconnection attempts
- Connection status indicator


┌─────────────────────────────────────────────────────────────┐
│ 1.3 Status Check Failure (Server Available but Unhealthy)   │
└─────────────────────────────────────────────────────────────┘

Trigger: Server responds but not ready (503, 500, etc.)
Location: MainWindow.MainWindow_Loaded()

Flow:
MainWindow_Loaded()
    ├─ connection_handler.StatusCheckAsync()
    │   ├─ GET /api/ezcade_client/main/
    │   ├─ response.IsSuccessStatusCode == false
    │   └─ return false
    │
    └─ IF (!StatusCheckAsync())
        ├─ MessageBox.Show("Not connected to server")
        └─ return; (early exit from Loaded event)

Consequence: 
- MainWindow visible but uninitialized
- Batch list empty
- Threads not created
- Application shell only, no functionality

User Action Required: Close and restart application


ERROR CATEGORY 2: DATA & PARSING
═══════════════════════════════════════════════════════════════════

┌─────────────────────────────────────────────────────────────┐
│ 2.1 Invalid Query Format                                    │
└─────────────────────────────────────────────────────────────┘

Trigger: Hardware sends malformed query string
Examples:
- "PR ABC123" (missing CodeType and Count)
- "PB MODEL1" (missing PcbModel, CodeType, Count)
- "XY UNKNOWN DM 5" (invalid Type)
- "PR ABC DM FIVE" (non-numeric Count)

Location: SerialNumberQuery constructor

Flow:
new SerialNumberQuery("PR ABC123")
    ├─ var parts = input.Split(' ')
    │   └─ parts = ["PR", "ABC123"]
    │
    ├─ IF Type == "PR"
    │   └─ IF parts.Length != 4
    │       └─ throw new FormatException("Input must contain exactly four parts")
    │
    └─ Exception thrown

Request_recived(recived_string)
    ├─ SerialNumberQuery request = new(recived_string)
    │   └─ Throws FormatException
    │
    └─ Caught by EZcade_Listen_Thread try/catch
        └─ catch { } (empty catch, silent fail)

Consequence: Thread terminates, no processing, silent failure

Current Behavior: No error displayed, listen thread dies
Improvement Needed:
- Log malformed queries
- MessageBox notification to operator
- Restart listen thread after error
- Send error response to hardware


┌─────────────────────────────────────────────────────────────┐
│ 2.2 Server Returns ERROR Serial                             │
└─────────────────────────────────────────────────────────────┘

Trigger: Server unable to generate serial (batch exhausted, etc.)
Location: Http_Connection_Handler.GetSerialNumber()

Flow:
Server Response:
{
  "serialNumber": "ERROR",
  "dmCode": ""
}

    ↓ Deserialized to DTO

SerialNumberDto dto = new() {
    SerialNumber = "ERROR",
    DmCode = ""
};

    ↓ Returned to caller

HandleQueryResponse(query, "ERROR", "", 1)
    ├─ var serialNumber_to_print = query.GetSerialNumber("ERROR", "")
    │   └─ Returns "PRERROR" (prefix applied)
    │
    └─ IF (IsAutomated)
        ├─ IF (SerialNumberObj.SerialNumber != "ERROR")
        │   └─ [Condition FALSE, SendAndPrint() skipped]
        │
        └─ QueryCount = query.Count - 1
            └─ HandleQueries("ERROR", "") still called

In Manual Mode:
    └─ serial_number_TextBox.Text = "PRERROR"
        ├─ Operator sees "PRERROR" in UI
        └─ Print button enabled (operator could accidentally send)

Consequence:
- Automated: Batch processes but no serials sent
- Manual: Operator sees error but can mistakenly print

Improvement Needed:
- Clearer error indication (red text, error icon)
- Disable Print button when SerialNumber == "ERROR"
- Skip HandleQueries() entirely on error
- Notification to operator with actionable message


┌─────────────────────────────────────────────────────────────┐
│ 2.3 JSON Deserialization Failure                            │
└─────────────────────────────────────────────────────────────┘

Trigger: Server returns unexpected JSON structure
Example: {"result": "error", "message": "Batch not found"}

Location: Http_Connection_Handler.GetSerialNumber()

Flow:
var dto = JsonSerializer.Deserialize&lt;SerialNumberDto&gt;(resultJson, ...)
    └─ Throws: JsonException
        Message: "The JSON value could not be converted to SerialNumberDto"

Current Handling: No try/catch around deserialization
    └─ Exception propagates to calling thread
        └─ Thread terminates

Consequence: Listen thread crashes, no error displayed

Improvement Needed:
try {
    var dto = JsonSerializer.Deserialize&lt;SerialNumberDto&gt;(resultJson);
    if (dto == null || string.IsNullOrEmpty(dto.SerialNumber))
        return new() { SerialNumber = "ERROR" };
    return dto;
}
catch (JsonException ex)
{
    MessageBox.Show($"Server response format error: {ex.Message}");
    return new() { SerialNumber = "ERROR" };
}


ERROR CATEGORY 3: BATCH & WORKFLOW
═══════════════════════════════════════════════════════════════════

┌─────────────────────────────────────────────────────────────┐
│ 3.1 No Batch Selected                                       │
└─────────────────────────────────────────────────────────────┘

Trigger: Operator forgets to select batch, leaves "Select Batch"
Location: Request_recived()

Flow:
string batch = firstComboBox.Dispatcher.Invoke(() => 
    firstComboBox.SelectedItem?.ToString());
    └─ batch = "Select Batch"

SerialNumberRequest request = new() {
    BatchName = "Select Batch",  ← Invalid batch name
    ProductModel = "ABC123",
    Type = "PR"
};

POST /api/ezcade_client/main/serialNumber
    └─ Server receives "Select Batch"
        ├─ Lookup fails (batch doesn't exist)
        └─ Returns ERROR or 404

Current Behavior: Request sent with invalid batch
Consequence: Server error, serial generation fails

Improvement Needed:
if (batch == "Select Batch" || string.IsNullOrEmpty(batch))
{
    MessageBox.Show("Please select a valid batch before processing queries.");
    return; // Skip processing
}


┌─────────────────────────────────────────────────────────────┐
│ 3.2 Activation Failure (Network or Server Error)            │
└─────────────────────────────────────────────────────────────┘

Trigger: Activation endpoint unavailable or serial invalid
Location: Http_Connection_Handler.Activation()

Flow:
Activation(serialNumber, "product")
    ├─ POST /api/ezcade_client/main/activation/product/SN00001
    │
    ├─ Scenario A: Network failure
    │   └─ HttpRequestException thrown
    │       └─ No try/catch, exception propagates
    │
    ├─ Scenario B: Server error
    │   ├─ response.StatusCode = 500
    │   └─ response.IsSuccessStatusCode = false
    │       └─ return false
    │
    └─ HandleQueries():
        var response = connection_handler.Activation(...);
        // 'response' captured but never checked

Current Behavior: Activation failure ignored, no notification
Consequence: Serial printed but not activated (data inconsistency)

Improvement Needed:
bool activated = connection_handler.Activation(serialNumber, "product");
if (!activated)
{
    string message = $"WARNING: Activation failed for {serialNumber}\n" +
                     "Serial was printed but NOT activated in system.\n" +
                     "Please manually activate or contact administrator.";
    MessageBox.Show(message, "Activation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
    
    // Log to file for audit trail
    LogError($"Activation failed: {serialNumber}, Type: product");
}


ERROR CATEGORY 4: THREAD & CONCURRENCY
═══════════════════════════════════════════════════════════════════

┌─────────────────────────────────────────────────────────────┐
│ 4.1 Thread Abort/Cancellation During Processing             │
└─────────────────────────────────────────────────────────────┘

Trigger: User closes application during batch processing
Location: Window_Closing event

Flow:
Window_Closing(sender, e)
    ├─ _cancellationTokenSource.Cancel()
    ├─ connection_handler.Cleanup()
    │   └─ _httpClient.Dispose()
    │
    └─ ezcade_Connection_Handler.Cleanup()
        ├─ Ezcadeclient.Close()
        └─ Ezcadeserver.Stop()

Meanwhile, HandleQueries() loop running:
    ├─ for (int i = 0; i &lt; QueryCount; i++)
    │   ├─ ezcade_Connection_Handler.Listen()
    │   │   └─ Ezcadestream.Read(...) ← Stream closed
    │   │       └─ Throws IOException
    │   │
    │   └─ Caught by empty catch { }
    │       └─ Loop continues (but stream dead)

Consequence: Batch partially processed, activation may not occur

Improvement Needed:
- Check CancellationToken before each iteration
- Gracefully complete current query before closing
- Save batch state for recovery
- Notify server of incomplete batch


┌─────────────────────────────────────────────────────────────┐
│ 4.2 Dispatcher Invoke Failure (UI Thread Deadlock)          │
└─────────────────────────────────────────────────────────────┘

Trigger: Rare concurrency issue, UI thread busy
Location: Various Dispatcher.Invoke() calls

Flow:
string batch = firstComboBox.Dispatcher.Invoke(() => 
    firstComboBox.SelectedItem?.ToString());
    └─ If UI thread deadlocked or unresponsive:
        └─ Invoke() blocks indefinitely

Current Behavior: Listen thread hangs, no timeout
Consequence: Application appears frozen

Improvement Needed:
var batch = firstComboBox.Dispatcher.Invoke(() => 
    firstComboBox.SelectedItem?.ToString(),
    DispatcherPriority.Normal,
    CancellationToken.None,
    TimeSpan.FromSeconds(5)); // 5 second timeout

if (batch == null)
{
    // UI thread not responding, use fallback
    batch = _lastKnownBatch;
}


ERROR RECOVERY SUMMARY
═══════════════════════════════════════════════════════════════════

Current State: Minimal error handling
- Many exceptions caught with empty catch { }
- Silent failures common
- No retry mechanisms
- Limited user feedback

Recommended Enhancements:
1. Structured logging (file-based)
2. Retry logic with exponential backoff
3. User notifications for all error states
4. Connection status monitoring
5. Batch recovery/resume capability
6. Server health checks
7. Audit trail for activation failures
```

---

## 3. State Management & UI Transitions

### UI State Machine

```
┌────────────────────────────────────────────────────────────────────┐
│ APPLICATION STATE MACHINE                                          │
└────────────────────────────────────────────────────────────────────┘

STATE: STARTUP (Initial)
═══════════════════════════════════════════════════════════════════
UI Elements:
    ├─ Edit_Button: "Connect"
    ├─ print_Buttun: Hidden
    ├─ Cancel_Button: Hidden
    ├─ serial_number_TextBox: "press connect and then start requesting..."
    ├─ firstComboBox: Populated with batches
    └─ Automation_CheckBox: Unchecked

Backend State:
    ├─ IsAutomated: false
    ├─ EZcade_Connection_Handler: null
    ├─ EZcade_Listen_Thread: Created but not started
    ├─ EZcade_Start_Thread: Created but not started
    └─ TCP Connection: Not established

Allowed Actions:
    └─ Click "Connect" button

Transitions:
    ├─ [Connect Button Click] → CONNECTING
    └─ [Window Close] → SHUTDOWN


STATE: CONNECTING
═══════════════════════════════════════════════════════════════════
Trigger: Edit_Button_Click() when Content == "Connect"

UI Changes:
    ├─ Edit_Button: "Connect" → (no change yet)
    ├─ print_Buttun: Hidden → Visible (but disabled)
    ├─ Cancel_Button: Hidden → Visible (but disabled)
    └─ serial_number_TextBox: "press connect..." → (no change)

Backend Actions:
    ├─ EZcade_Start_Thread.Start()
    │   ├─ EZcade_Connection_Handler = new()
    │   │   └─ TcpListener started on 127.0.0.1:1000
    │   │
    │   ├─ ezcade_Connection_Handler.init()
    │   │   └─ [BLOCKS until hardware connects]
    │   │
    │   └─ EZcade_Listen_Thread.Start()
    │       └─ Listen() called (blocks for data)
    │
    └─ Edit_Button.Content = "Edit"

State Duration: Varies (waiting for hardware connection)

Transitions:
    ├─ [Hardware Connects] → IDLE_CONNECTED
    └─ [Connection Timeout/Error] → ERROR_STATE (not implemented)


STATE: IDLE_CONNECTED
═══════════════════════════════════════════════════════════════════
Trigger: TCP connection established, listening for requests

UI State:
    ├─ Edit_Button: "Edit" (enabled)
    ├─ print_Buttun: Visible but disabled
    ├─ Cancel_Button: Visible but disabled
    ├─ serial_number_TextBox: "press connect..." (read-only)
    └─ firstComboBox: Enabled

Backend State:
    ├─ EZcade_Connection_Handler: Active, connected
    ├─ EZcade_Listen_Thread: Running, blocked on Listen()
    └─ TCP Stream: Open, waiting for data

Allowed Actions:
    ├─ Select different batch
    ├─ Toggle Automation checkbox
    └─ [Wait for hardware request]

Transitions:
    ├─ [Hardware Sends Request] → PROCESSING_REQUEST
    └─ [Window Close] → SHUTDOWN


STATE: PROCESSING_REQUEST (Manual Mode)
═══════════════════════════════════════════════════════════════════
Trigger: Hardware query received, IsAutomated == false

UI State:
    ├─ Edit_Button: "Edit" (enabled)
    ├─ print_Buttun: Enabled
    ├─ Cancel_Button: Enabled
    └─ serial_number_TextBox: "PRSN00001" (formatted serial displayed)

Backend State:
    ├─ SerialNumberObj: Populated with server response
    ├─ QueryCount: Set to (Count - 1)
    ├─ QueryType: Set to query Type ("PR", "PB", "CO")
    └─ EZcade_Listen_Thread: Terminated (waiting for user action)

Allowed Actions:
    ├─ Click "Print" → PRINTING
    ├─ Click "Edit" → EDITING
    └─ Click "Cancel" → CANCELLED

Visual Representation:
┌─────────────────────────────────────────────┐
│ Serial Number:  PRSN00001                   │
│ ┌─────────────────────────────────────────┐ │
│ │ [Edit]  [Print]  [Cancel]               │ │
│ │   ✓       ✓         ✓                   │ │
│ └─────────────────────────────────────────┘ │
└─────────────────────────────────────────────┘

Transitions:
    ├─ [Print Click] → PRINTING
    ├─ [Edit Click] → EDITING
    └─ [Cancel Click] → IDLE_CONNECTED


STATE: EDITING
═══════════════════════════════════════════════════════════════════
Trigger: Edit_Button_Click() when Content == "Edit"

UI Changes:
    ├─ Edit_Button: "Edit" → "OK"
    ├─ print_Buttun: Enabled → Disabled
    ├─ Cancel_Button: Enabled → Disabled
    └─ serial_number_TextBox: Read-only → Editable

Backend State:
    └─ (No changes, waiting for user edit)

User Actions:
    ├─ Type in TextBox (modify serial)
    └─ Click "OK" when done

Visual:
┌─────────────────────────────────────────────┐
│ Serial Number:  PRCU|STOM123                │
│                     ↑ cursor                │
│ ┌─────────────────────────────────────────┐ │
│ │ [ OK ]  [Print]  [Cancel]               │ │
│ │   ✓       ✗         ✗                   │ │
│ └─────────────────────────────────────────┘ │
└─────────────────────────────────────────────┘

Transitions:
    └─ [OK Click] → PROCESSING_REQUEST (with modified serial)


STATE: PRINTING
═══════════════════════════════════════════════════════════════════
Trigger: print_Buttun_Click()

UI Changes (Immediate):
    ├─ Edit_Button: Disabled
    ├─ print_Buttun: Disabled
    ├─ Cancel_Button: Disabled
    └─ serial_number_TextBox: (displays current serial)

Backend Actions:
    ├─ SendAndPrint(serial_number_TextBox.Text)
    │   ├─ Write to TCP stream
    │   └─ MessageBox.Show("printed " + serial)
    │
    ├─ HandleQueries(RemovePrefix(serial), dmCode)
    │   └─ FOR remaining QueryCount iterations:
    │       ├─ Listen() for next hardware request
    │       ├─ Parse query
    │       ├─ Format same serial
    │       └─ SendAndPrint()
    │
    └─ serial_number_TextBox.Text = "start requesting in EZcade"

State Duration: Depends on QueryCount (blocking)

Transitions:
    └─ [All Queries Complete] → IDLE_CONNECTED


STATE: CANCELLED
═══════════════════════════════════════════════════════════════════
Trigger: Cancel_Button_Click()

UI Changes:
    ├─ Edit_Button: Disabled
    ├─ print_Buttun: Disabled
    ├─ Cancel_Button: Disabled
    └─ serial_number_TextBox: "start requesting in EZcade"

Backend Actions:
    ├─ Recreate EZcade_Listen_Thread
    └─ Start new Listen() cycle

Transitions:
    └─ [Thread Started] → IDLE_CONNECTED


STATE: AUTOMATED_PROCESSING (Automated Mode)
═══════════════════════════════════════════════════════════════════
Trigger: Hardware request received, IsAutomated == true

UI State:
    ├─ Edit_Button: Disabled
    ├─ print_Buttun: Disabled
    ├─ Cancel_Button: Disabled
    └─ serial_number_TextBox: (not updated, still shows previous text)

Backend Actions:
    ├─ GetSerialNumber() from server
    ├─ Immediate SendAndPrint() (no UI interaction)
    ├─ HandleQueries() loop:
    │   └─ FOR each remaining query:
    │       ├─ Listen()
    │       ├─ Format serial
    │       └─ SendAndPrint()
    │
    ├─ Activation(serialNumber, fullType)
    │
    └─ Restart EZcade_Listen_Thread

Visual (No Change):
┌─────────────────────────────────────────────┐
│ Serial Number:  start requesting in EZcade  │
│ ┌─────────────────────────────────────────┐ │
│ │ [Edit]  [Print]  [Cancel]               │ │
│ │   ✗       ✗         ✗                   │ │
│ └─────────────────────────────────────────┘ │
│                                             │
│ [Background processing: MessageBoxes show   │
│  "printed SN00001" for each query]          │
└─────────────────────────────────────────────┘

State Duration: QueryCount × (Network Latency)

Transitions:
    └─ [Batch Complete] → IDLE_CONNECTED


STATE: SHUTDOWN
═══════════════════════════════════════════════════════════════════
Trigger: Window_Closing event

Actions:
    ├─ connection_handler.Cleanup()
    │   └─ _httpClient.Dispose()
    │
    ├─ _cancellationTokenSource.Cancel()
    │
    └─ ezcade_Connection_Handler.Cleanup()
        ├─ Ezcadeclient.Close()
        └─ Ezcadeserver.Stop()

Result: Application terminates
```

### State Transition Matrix

```
┌─────────────────────┬──────────────────────────────────────────────┐
│ Current State       │ Possible Transitions                         │
├─────────────────────┼──────────────────────────────────────────────┤
│ STARTUP             │ → CONNECTING (Connect click)                 │
│                     │ → SHUTDOWN (Window close)                    │
├─────────────────────┼──────────────────────────────────────────────┤
│ CONNECTING          │ → IDLE_CONNECTED (Hardware connects)         │
│                     │ → ERROR_STATE (Connection fails)*            │
├─────────────────────┼──────────────────────────────────────────────┤
│ IDLE_CONNECTED      │ → PROCESSING_REQUEST (Manual, request arrives)│
│                     │ → AUTOMATED_PROCESSING (Auto, request arrives)│
│                     │ → SHUTDOWN (Window close)                    │
├─────────────────────┼──────────────────────────────────────────────┤
│ PROCESSING_REQUEST  │ → PRINTING (Print click)                     │
│                     │ → EDITING (Edit click)                       │
│                     │ → CANCELLED (Cancel click)                   │
├─────────────────────┼──────────────────────────────────────────────┤
│ EDITING             │ → PROCESSING_REQUEST (OK click)              │
├─────────────────────┼──────────────────────────────────────────────┤
│ PRINTING            │ → IDLE_CONNECTED (Batch complete)            │
├─────────────────────┼──────────────────────────────────────────────┤
│ CANCELLED           │ → IDLE_CONNECTED (Thread restarted)          │
├─────────────────────┼──────────────────────────────────────────────┤
│ AUTOMATED_PROCESSING│ → IDLE_CONNECTED (Batch complete)            │
└─────────────────────┴──────────────────────────────────────────────┘

* ERROR_STATE not currently implemented (improvement opportunity)
```

---

## 4. Configuration & Parameters

### System Configuration

```
┌────────────────────────────────────────────────────────────────────┐
│ CONFIGURATION PARAMETERS                                           │
└────────────────────────────────────────────────────────────────────┘

HTTP SERVER CONFIGURATION
═══════════════════════════════════════════════════════════════════
File: Http_Connection_Handler.cs

Base Address:  http://192.168.1.122:8080/
Base Route:    api/ezcade_client/main/

Endpoints:
├─ POST /login
│   Purpose: Authentication, token issuance
│   Payload: { "Username": "...", "Password": "..." }
│   Response: { "myToken": "..." }
│
├─ GET /api/ezcade_client/main/
│   Purpose: Health check / status validation
│   Response: 200 OK (server healthy)
│
├─ GET /api/ezcade_client/main/ComponentBatches
│   Purpose: Retrieve available batch list
│   Response: { "items": ["BATCH_001", "BATCH_002", ...] }
│
├─ POST /api/ezcade_client/main/serialNumber
│   Purpose: Generate serial number
│   Payload: { "batchName": "...", "productModel": "...", ... }
│   Response: { "serialNumber": "...", "dmCode": "..." }
│
└─ POST /api/ezcade_client/main/activation/{fullType}/{serialNumber}
    Purpose: Activate serial after printing
    fullType: "product" or "pcb"
    Response: 200 OK (activation successful)

Hardcoded Values (Improvement Opportunity):
- Server IP: 192.168.1.122 (should be configurable)
- Server Port: 8080 (should be configurable)
- Timeout: None set (uses default)
- Retry Policy: None (should implement)


TCP HARDWARE CONFIGURATION
═══════════════════════════════════════════════════════════════════
File: EZcade_Connection_Handler.cs

IP Address:    127.0.0.1 (localhost)
Port:          1000
Protocol:      TCP
Buffer Size:   1024 bytes
Encoding:      ASCII

Socket Options:
└─ ReuseAddress: true
   Purpose: Allow immediate rebind after connection close

Connection Model:
├─ Server Mode: Application acts as TCP server
├─ Client:      EZcade hardware connects as client
└─ Persistence: Single connection, persistent throughout session

Hardcoded Values:
string EzcadeIp = "127.0.0.1";    // Localhost only
int EzcadePort = 1000;             // Fixed port

Configuration Needs:
- Make IP/Port configurable (App.config)
- Support remote hardware (not just localhost)
- Timeout configuration for Listen()


CREDENTIAL PERSISTENCE
═══════════════════════════════════════════════════════════════════
Storage: Properties.Settings (user-scoped)
Location: C:\Users\[Username]\AppData\Local\[Publisher]\[AppName]\[Version]\user.config

Settings:
├─ SavedUsername: string
└─ SavedPassword: string

Security Considerations:
⚠ WARNING: Passwords stored in PLAINTEXT
⚠ Location: Accessible to user and administrator
⚠ Encryption: NONE

Recommended Improvements:
1. Use Windows Credential Manager (CredentialManagement NuGet)
2. Encrypt passwords using DPAPI (System.Security.Cryptography.ProtectedData)
3. Store only tokens, not passwords
4. Implement token refresh mechanism

Example Encrypted Storage:
byte[] encryptedPassword = ProtectedData.Protect(
    Encoding.UTF8.GetBytes(password),
    null,
    DataProtectionScope.CurrentUser);
Settings.Default.SavedPassword = Convert.ToBase64String(encryptedPassword);


APPLICATION UI CONFIGURATION
═══════════════════════════════════════════════════════════════════
File: App.xaml

Window Size:    Not specified (default)
Theme:          Custom styles defined in App.xaml
Icon:           logo.ico

Styles Defined:
├─ Button Styles (ControlzEx integration)
├─ TextBox Styles
├─ ComboBox Styles
└─ Color Schemes

Hardcoded UI Text:
- "press connect and then start requesting in EZcade"
- "start requesting in EZcade"
- "Not connected to server"
- "Login Failed."
- "printed [serial]"

Localization: Not implemented (English only)


THREAD CONFIGURATION
═══════════════════════════════════════════════════════════════════
Thread Model: Explicit Thread creation (not ThreadPool or Task)

Threads:
├─ EZcade_Start_Thread
│   Purpose: Initialize TCP connection
│   Lifecycle: Runs once at Connect button click
│   Termination: Natural completion after starting Listen thread
│
└─ EZcade_Listen_Thread
    Purpose: Receive hardware requests
    Lifecycle: Recreated after each batch cycle
    Termination: Natural completion, or exception

Cancellation:
└─ CancellationTokenSource _cancellationTokenSource
    Purpose: Signal shutdown during Window_Closing
    Usage: Limited (checked in EZcade_Start_Thread only)

Improvement Opportunities:
- Use async/await with CancellationToken throughout
- Implement proper graceful shutdown
- Use Task instead of Thread
- Add timeout mechanisms


LOGGING & DIAGNOSTICS
═══════════════════════════════════════════════════════════════════
Current State: NO LOGGING IMPLEMENTED

Logging Needs:
├─ Application startup/shutdown
├─ Login attempts (success/failure)
├─ Hardware connection events
├─ Query processing (received query, sent serial)
├─ Server API calls (request/response)
├─ Error/exception details
└─ Activation attempts (success/failure)

Recommended Implementation:
- NLog or Serilog library
- Log to file in application directory
- Configurable log levels (Debug, Info, Warning, Error)
- Log rotation (daily, size-based)

Example Configuration (NLog):
&lt;target name="logfile" xsi:type="File" 
        fileName="${basedir}/logs/ezcade_${shortdate}.log" /&gt;


FEATURE FLAGS
═══════════════════════════════════════════════════════════════════
Runtime Flags:
├─ IsAutomated: bool (controlled by UI checkbox)
│   Default: false
│   Purpose: Toggle manual vs automated processing
│
└─ IsDataMatrixActive: bool (controlled by UI checkbox)
    Default: false
    Purpose: [Currently unused, intended for future use]

Missing Flags (Potential):
- EnableDebugMode: Show verbose diagnostics
- AllowSerialEdit: Enable/disable Edit button
- RequireActivation: Make activation mandatory
- StrictBatchValidation: Reject queries if batch not selected
```

---

## 5. Architecture Components

### Component Diagram

```
┌────────────────────────────────────────────────────────────────────┐
│ EZCADE_CLIENT ARCHITECTURE                                         │
└────────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────┐
│ PRESENTATION LAYER (WPF)                                         │
├─────────────────────────────────────────────────────────────────┤
│                                                                  │
│  ┌───────────────────────────────────────────────────────────┐  │
│  │ App.xaml                                                   │  │
│  │ - Global styles (ControlzEx integration)                  │  │
│  │ - Application entry point                                 │  │
│  │ - Resource dictionaries                                   │  │
│  └───────────────────────────────────────────────────────────┘  │
│                          ↓                                       │
│  ┌───────────────────────────────────────────────────────────┐  │
│  │ LoginWindow.xaml                                          │  │
│  │ - Credential input form                                   │  │
│  │ - Username/Password fields                                │  │
│  │ - Login button                                            │  │
│  └───────────────────────────────────────────────────────────┘  │
│                          ↓                                       │
│  ┌───────────────────────────────────────────────────────────┐  │
│  │ MainWindow.xaml                                           │  │
│  │ ┌───────────────────────────────────────────────────────┐ │  │
│  │ │ UI Controls:                                          │ │  │
│  │ │ - firstComboBox: Batch selection dropdown            │ │  │
│  │ │ - serial_number_TextBox: Serial display/edit         │ │  │
│  │ │ - Edit_Button: Connect/Edit/OK toggle button         │ │  │
│  │ │ - print_Buttun: Send serial to hardware              │ │  │
│  │ │ - Cancel_Button: Cancel current operation            │ │  │
│  │ │ - Automation_CheckBox: Toggle automated mode         │ │  │
│  │ │ - DataMatrix_CheckBox: Toggle DM mode (unused)       │ │  │
│  │ │ - ip_Lable: Server connection status                 │ │  │
│  │ └───────────────────────────────────────────────────────┘ │  │
│  └───────────────────────────────────────────────────────────┘  │
│                                                                  │
└─────────────────────────────────────────────────────────────────┘
                          ↓
┌─────────────────────────────────────────────────────────────────┐
│ APPLICATION LAYER (Code-Behind)                                  │
├─────────────────────────────────────────────────────────────────┤
│                                                                  │
│  ┌───────────────────────────────────────────────────────────┐  │
│  │ MainWindow.xaml.cs                                        │  │
│  │ ┌───────────────────────────────────────────────────────┐ │  │
│  │ │ State Management:                                     │ │  │
│  │ │ - IsAutomated: bool                                   │ │  │
│  │ │ - IsDataMatrixActive: bool                            │ │  │
│  │ │ - QueryCount: int                                     │ │  │
│  │ │ - QueryType: string                                   │ │  │
│  │ │ - SerialNumberObj: SerialNumberDto                    │ │  │
│  │ └───────────────────────────────────────────────────────┘ │  │
│  │ ┌───────────────────────────────────────────────────────┐ │  │
│  │ │ Event Handlers:                                       │ │  │
│  │ │ - MainWindow_Loaded()                                 │ │  │
│  │ │ - Edit_Button_Click()                                 │ │  │
│  │ │ - print_Buttun_Click()                                │ │  │
│  │ │ - Cancel_Button_Click()                               │ │  │
│  │ │ - Automation_CheckBox_Checked/Unchecked()            │ │  │
│  │ │ - Window_Closing()                                    │ │  │
│  │ └───────────────────────────────────────────────────────┘ │  │
│  │ ┌───────────────────────────────────────────────────────┐ │  │
│  │ │ Business Logic:                                       │ │  │
│  │ │ - AutoLogin()                                         │ │  │
│  │ │ - Request_recived()                                   │ │  │
│  │ │ - HandleQueryResponse()                               │ │  │
│  │ │ - HandleQueries()                                     │ │  │
│  │ │ - RemovePrefix()                                      │ │  │
│  │ └───────────────────────────────────────────────────────┘ │  │
│  │ ┌───────────────────────────────────────────────────────┐ │  │
│  │ │ Threading:                                            │ │  │
│  │ │ - EZcade_Start_Thread: Thread                         │ │  │
│  │ │ - EZcade_Listen_Thread: Thread                        │ │  │
│  │ │ - _cancellationTokenSource: CancellationTokenSource   │ │  │
│  │ └───────────────────────────────────────────────────────┘ │  │
│  └───────────────────────────────────────────────────────────┘  │
│                                                                  │
│  ┌───────────────────────────────────────────────────────────┐  │
│  │ LoginWindow.xaml.cs                                       │  │
│  │ - LoginButton_Click()                                     │  │
│  │ - Credential validation                                   │  │
│  │ - Settings persistence                                    │  │
│  └───────────────────────────────────────────────────────────┘  │
│                                                                  │
└─────────────────────────────────────────────────────────────────┘
              ↓                                    ↓
┌─────────────────────────────────┐  ┌──────────────────────────────┐
│ BUSINESS LOGIC LAYER            │  │ DOMAIN MODEL LAYER           │
├─────────────────────────────────┤  ├──────────────────────────────┤
│                                 │  │                              │
│ ┌─────────────────────────────┐ │  │ ┌──────────────────────────┐ │
│ │ SerialNumberQuery.cs        │ │  │ │ EzcadeDtos/              │ │
│ │ ┌─────────────────────────┐ │ │  │ │                          │ │
│ │ │ Properties:             │ │ │  │ │ SerialNumberDto.cs       │ │
│ │ │ - ProductModel: string  │ │ │  │ │ - SerialNumber: string   │ │
│ │ │ - PcbModel: string?     │ │ │  │ │ - DmCode: string         │ │
│ │ │ - Type: string          │ │ │  │ │                          │ │
│ │ │ - CodeType: string      │ │ │  │ │ SerialNumberRequest.cs   │ │
│ │ │ - Count: int            │ │ │  │ │ - BatchName: string      │ │
│ │ └─────────────────────────┘ │ │  │ │ - ProductModel: string   │ │
│ │ ┌─────────────────────────┐ │ │  │ │ - Type: string           │ │
│ │ │ Methods:                │ │ │  │ │ - PcbModel: string?      │ │
│ │ │ - Constructor(string)   │ │ │  │ │                          │ │
│ │ │   Parse query string    │ │ │  │ │ BatchDto.cs              │ │
│ │ │ - GetSerialNumber()     │ │ │  │ │ - BatchName: string      │ │
│ │ │   Format output         │ │ │  │ │ - BatchParts: List<>     │ │
│ │ └─────────────────────────┘ │ │  │ │                          │ │
│ └─────────────────────────────┘ │  │ │ ListDto<T>.cs            │ │
│                                 │  │ │ - Items: List<T>         │ │
└─────────────────────────────────┘  │ └──────────────────────────┘ │
                                     └──────────────────────────────┘
              ↓                                    ↓
┌─────────────────────────────────────────────────────────────────┐
│ INFRASTRUCTURE LAYER (Communication)                             │
├─────────────────────────────────────────────────────────────────┤
│                                                                  │
│  ┌──────────────────────────┐     ┌─────────────────────────┐   │
│  │ HttpConnectionHandler.cs │     │ EZcade_Connection_      │   │
│  │                          │     │ Handler.cs              │   │
│  │ [REST API Client]        │     │ [TCP Server]            │   │
│  ├──────────────────────────┤     ├─────────────────────────┤   │
│  │ Fields:                  │     │ Fields:                 │   │
│  │ - _httpClient            │     │ - EzcadeIp: 127.0.0.1   │   │
│  │ - _base_address          │     │ - EzcadePort: 1000      │   │
│  │ - _base_route            │     │ - Ezcadeserver          │   │
│  │ - _token                 │     │ - Ezcadeclient          │   │
│  │ - ServerConnectionStatus │     │ - Ezcadestream          │   │
│  ├──────────────────────────┤     ├─────────────────────────┤   │
│  │ Methods:                 │     │ Methods:                │   │
│  │ - LoginAsync()           │     │ - init()                │   │
│  │   POST /login            │     │   AcceptTcpClient()     │   │
│  │ - StatusCheckAsync()     │     │ - Listen()              │   │
│  │   GET /main/             │     │   Read from stream      │   │
│  │ - GetComponentBatchList()│     │ - SendAndPrint()        │   │
│  │   GET /ComponentBatches  │     │   Write to stream       │   │
│  │ - GetSerialNumber()      │     │ - Cleanup()             │   │
│  │   POST /serialNumber     │     │   Close connections     │   │
│  │ - Activation()           │     └─────────────────────────┘   │
│  │   POST /activation/{}/{} │                                   │
│  │ - Cleanup()              │                                   │
│  └──────────────────────────┘                                   │
│             ↓                              ↓                     │
│  ┌──────────────────────┐      ┌────────────────────────┐       │
│  │ System.Net.Http      │      │ System.Net.Sockets     │       │
│  │ - HttpClient         │      │ - TcpListener          │       │
│  │ - HttpRequestMessage │      │ - TcpClient            │       │
│  │ - HttpResponse       │      │ - NetworkStream        │       │
│  └──────────────────────┘      └────────────────────────┘       │
│                                                                  │
└─────────────────────────────────────────────────────────────────┘
              ↓                                    ↓
┌─────────────────────────────────┐  ┌──────────────────────────────┐
│ EXTERNAL: REST API SERVER       │  │ EXTERNAL: EZCADE HARDWARE    │
│ http://192.168.1.122:8080       │  │ TCP Client → 127.0.0.1:1000  │
│                                 │  │                              │
│ - Authentication Service        │  │ - Sends query strings        │
│ - Batch Management Service      │  │ - Receives formatted serials │
│ - Serial Generation Service     │  │ - Triggers printing          │
│ - Activation Service            │  │                              │
└─────────────────────────────────┘  └──────────────────────────────┘

┌─────────────────────────────────────────────────────────────────┐
│ DATA PERSISTENCE LAYER                                           │
├─────────────────────────────────────────────────────────────────┤
│                                                                  │
│  ┌───────────────────────────────────────────────────────────┐  │
│  │ Properties.Settings (User-Scoped)                         │  │
│  │ Location: AppData\Local\[Publisher]\[App]\user.config     │  │
│  │                                                            │  │
│  │ - SavedUsername: string                                   │  │
│  │ - SavedPassword: string (⚠ PLAINTEXT)                     │  │
│  └───────────────────────────────────────────────────────────┘  │
│                                                                  │
└─────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────┐
│ LEGACY COMPONENTS (Not Used in Current Flow)                    │
├─────────────────────────────────────────────────────────────────┤
│                                                                  │
│  ┌───────────────────────────────────────────────────────────┐  │
│  │ Connection_Handler.cs                                     │  │
│  │ - Legacy encrypted TCP connection handler                │  │
│  │ - JsonObject-based request/response                      │  │
│  │ - Not used in HTTP-based flow                            │  │
│  └───────────────────────────────────────────────────────────┘  │
│                                                                  │
└─────────────────────────────────────────────────────────────────┘
```

### Component Responsibilities

#### Presentation Layer Components

**App.xaml / App.xaml.cs**
- Responsibilities:
  - Application-wide style definitions
  - Global resource dictionaries
  - Application lifecycle management
  - Entry point for WPF application
- Dependencies: ControlzEx (styling library)

**LoginWindow.xaml / LoginWindow.xaml.cs**
- Responsibilities:
  - User credential capture
  - Credential validation
  - Save credentials to Settings
  - Trigger login via HttpConnectionHandler
- Lifecycle: Modal dialog, shown on demand

**MainWindow.xaml / MainWindow.xaml.cs**
- Responsibilities:
  - Primary operator interface
  - Batch selection
  - Serial number display/editing
  - Mode toggles (Automation, DataMatrix)
  - Thread management (Start/Listen)
  - Request orchestration
  - UI state management
- Lifecycle: Application lifetime (singleton)

#### Business Logic Components

**SerialNumberQuery.cs**
- Responsibilities:
  - Parse raw hardware query strings
  - Extract components: Type, Model(s), CodeType, Count
  - Format serial numbers with appropriate prefixes
  - Handle special cases (CO type with $ placeholder)
- Input: String (e.g., "PR ABC123 DM 5")
- Output: Structured query object + formatted serial

#### Domain Model Components

**SerialNumberDto.cs**
- Purpose: Data transfer from server API
- Properties: SerialNumber, DmCode
- Serialization: JSON (System.Text.Json)

**SerialNumberRequest.cs**
- Purpose: Data transfer to server API
- Properties: BatchName, ProductModel, Type, PcbModel
- Serialization: JSON

**BatchDto.cs / ListDto.cs**
- Purpose: Batch metadata and list containers
- Usage: Batch selection dropdown population

#### Infrastructure Components

**HttpConnectionHandler.cs**
- Responsibilities:
  - HTTP client lifecycle management
  - Authentication (token-based)
  - REST API communication
  - Server health checks
  - Serial number requests
  - Activation requests
- Protocol: HTTP/HTTPS
- Endpoint: Configurable base address (currently hardcoded)

**EZcade_Connection_Handler.cs**
- Responsibilities:
  - TCP server initialization
  - Client connection acceptance
  - Bidirectional communication with hardware
  - ASCII encoding/decoding
  - Connection lifecycle management
- Protocol: TCP
- Mode: Server (listens for hardware connections)

### Data Flow Diagram

```
┌─────────────────────────────────────────────────────────────────┐
│ REQUEST FLOW: Hardware Query → Serial Response                  │
└─────────────────────────────────────────────────────────────────┘

[1] EZcade Hardware
      ↓ TCP: "PR ABC123 DM 5"
[2] EZcade_Connection_Handler.Listen()
      ↓ ASCII decode
[3] MainWindow.Request_recived(string)
      ↓ ToUpper(), Get Batch
[4] SerialNumberQuery(string)
      ↓ Parse
[5] HttpConnectionHandler.GetSerialNumber(query, batch)
      ↓ Build SerialNumberRequest DTO
[6] POST /api/ezcade_client/main/serialNumber
      ↓ JSON serialize
[7] Server API
      ↓ Process, generate serial
[8] Server API Response
      ↓ JSON deserialize
[9] SerialNumberDto
      ↓ Extract fields
[10] SerialNumberQuery.GetSerialNumber(sn, dm)
      ↓ Apply formatting rules
[11] Formatted Serial String
      ↓ Display (Manual) OR SendAndPrint (Auto)
[12] EZcade_Connection_Handler.SendAndPrint(string)
      ↓ ASCII encode, TCP write
[13] EZcade Hardware
      ↓ Print/Process
```

### Dependency Graph

```
MainWindow.xaml.cs
  ├─ HttpConnectionHandler
  │   └─ System.Net.Http.HttpClient
  ├─ EZcade_Connection_Handler
  │   └─ System.Net.Sockets (TcpListener, TcpClient)
  ├─ SerialNumberQuery
  ├─ SerialNumberDto
  ├─ SerialNumberRequest
  ├─ BatchDto / ListDto
  └─ Properties.Settings

LoginWindow.xaml.cs
  ├─ HttpConnectionHandler
  └─ Properties.Settings

SerialNumberQuery
  └─ (No external dependencies)

HttpConnectionHandler
  ├─ System.Net.Http
  ├─ System.Text.Json
  └─ DTO classes

EZcade_Connection_Handler
  ├─ System.Net.Sockets
  ├─ System.Text (Encoding)
  └─ System.Windows (MessageBox, Application.Current.Dispatcher)
```

---

## 6. Future Considerations

### High-Priority Enhancements

#### 1. Configuration Management
**Current Issue**: Hardcoded server IP, port, and connection parameters  
**Proposal**: 
```xml
<!-- App.config -->
<appSettings>
  <add key="ServerBaseAddress" value="http://192.168.1.122:8080/" />
  <add key="ServerRoute" value="api/ezcade_client/main/" />
  <add key="EZcadeIP" value="127.0.0.1" />
  <add key="EZcadePort" value="1000" />
  <add key="RequestTimeout" value="30000" />
</appSettings>
```
**Benefits**:
- No recompilation for environment changes
- Support for dev/staging/production configurations
- Easier deployment

#### 2. Comprehensive Logging
**Current Issue**: No logging, debugging difficult, no audit trail  
**Proposal**: Implement NLog or Serilog
```csharp
public class LoggingService
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    
    public void LogQueryReceived(string query) 
    {
        Logger.Info($"Query received: {query}");
    }
    
    public void LogSerialGenerated(string serial, string batch) 
    {
        Logger.Info($"Serial generated: {serial} for batch {batch}");
    }
    
    public void LogActivationFailed(string serial, string error) 
    {
        Logger.Error($"Activation failed for {serial}: {error}");
    }
}
```
**Benefits**:
- Troubleshooting production issues
- Compliance and audit requirements
- Performance monitoring

#### 3. Enhanced Error Handling
**Current Issue**: Silent failures, empty catch blocks  
**Proposal**: Structured error handling with user notifications
```csharp
public class ErrorHandler
{
    public static void Handle(Exception ex, string context)
    {
        Logger.Error(ex, $"Error in {context}");
        
        var userMessage = ex switch
        {
            HttpRequestException => "Server connection lost. Please check network.",
            JsonException => "Server returned invalid data. Contact administrator.",
            FormatException => "Invalid query format received from hardware.",
            _ => "An unexpected error occurred. Please try again."
        };
        
        MessageBox.Show(userMessage, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
    }
}
```
**Benefits**:
- Operator awareness of issues
- Actionable error messages
- Improved reliability

#### 4. Credential Security
**Current Issue**: Plaintext password storage  
**Proposal**: Use Windows Credential Manager or DPAPI
```csharp
public class SecureCredentialStore
{
    public void SaveCredentials(string username, string password)
    {
        byte[] encrypted = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(password),
            null,
            DataProtectionScope.CurrentUser
        );
        
        Settings.Default.SavedUsername = username;
        Settings.Default.SavedEncryptedPassword = Convert.ToBase64String(encrypted);
        Settings.Default.Save();
    }
    
    public string GetPassword()
    {
        byte[] encrypted = Convert.FromBase64String(Settings.Default.SavedEncryptedPassword);
        byte[] decrypted = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(decrypted);
    }
}
```
**Benefits**:
- Security compliance
- Protection against credential theft
- User trust

### Medium-Priority Enhancements

#### 5. Retry Logic & Resilience
**Proposal**: Implement Polly for retry policies
```csharp
var retryPolicy = Policy
    .Handle<HttpRequestException>()
    .WaitAndRetryAsync(3, 
        retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
        onRetry: (exception, timeSpan, retryCount, context) =>
        {
            Logger.Warn($"Retry {retryCount} after {timeSpan.TotalSeconds}s due to {exception.Message}");
        });

await retryPolicy.ExecuteAsync(async () =>
{
    return await _httpClient.PostAsync(url, content);
});
```

#### 6. Batch Processing History
**Proposal**: Persist batch processing records locally
```csharp
public class BatchHistory
{
    public string BatchName { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public int QueryCount { get; set; }
    public List<string> SerialNumbers { get; set; }
    public bool ActivationSuccessful { get; set; }
}

// Store in SQLite or JSON file
```
**Benefits**:
- Audit trail
- Resume interrupted batches
- Performance metrics

#### 7. Connection Status Indicators
**Proposal**: Real-time status display
```xaml
<StatusBar>
    <StatusBarItem>
        <StackPanel Orientation="Horizontal">
            <Ellipse x:Name="ServerStatusIndicator" Width="10" Height="10" Fill="Green" />
            <TextBlock Text="Server: Connected" Margin="5,0,0,0" />
        </StackPanel>
    </StatusBarItem>
    <StatusBarItem>
        <StackPanel Orientation="Horizontal">
            <Ellipse x:Name="HardwareStatusIndicator" Width="10" Height="10" Fill="Red" />
            <TextBlock Text="Hardware: Disconnected" Margin="5,0,0,0" />
        </StackPanel>
    </StatusBarItem>
</StatusBar>
```

#### 8. Async/Await Conversion
**Current Issue**: Synchronous blocking calls with .GetAwaiter().GetResult()  
**Proposal**: Convert to proper async/await
```csharp
public async Task<bool> LoginAsync(string username, string password)
{
    var request = new { Username = username, Password = password };
    var content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");
    
    var response = await _httpClient.PostAsync("login", content);
    
    if (!response.IsSuccessStatusCode)
        return false;
    
    var json = await response.Content.ReadAsStringAsync();
    var root = JsonSerializer.Deserialize<JsonObject>(json);
    _token = root["myToken"]?.GetValue<string>();
    
    // ... rest of logic
}
```

### Low-Priority / Nice-to-Have

#### 9. Multi-Language Support (Localization)
**Proposal**: Implement resource files for internationalization
```csharp
// Strings.resx, Strings.es.resx, Strings.fr.resx
public string GetLocalizedString(string key)
{
    return Resources.ResourceManager.GetString(key, CultureInfo.CurrentUICulture);
}
```

#### 10. Batch Simulation Mode
**Proposal**: Test mode without hardware
```csharp
public class SimulatedHardwareConnection : IEZcadeConnection
{
    public string Listen()
    {
        // Simulate queries from file or configuration
        return "PR TEST123 DM 5";
    }
    
    public void SendAndPrint(string serial, bool showMessage = true)
    {
        Logger.Info($"[SIMULATED] Would print: {serial}");
    }
}
```
**Benefits**:
- Training without hardware
- Automated testing
- Development without dependencies

#### 11. Data Matrix Checkbox Functionality
**Current State**: Checkbox exists but IsDataMatrixActive never used  
**Proposal**: Implement override behavior
```csharp
private void HandleQueryResponse(SerialNumberQuery query, string serialNumber, string dmCode, int index)
{
    // Allow operator to force DM or SN mode regardless of query
    var effectiveCodeType = IsDataMatrixActive ? "DM" : query.CodeType;
    
    var serialNumber_to_print = query.GetSerialNumber(
        serialNumber, 
        dmCode, 
        overrideCodeType: effectiveCodeType
    );
    
    // ... rest of logic
}
```

#### 12. Real-Time Performance Metrics
**Proposal**: Display throughput and timing statistics
```xaml
<StackPanel>
    <TextBlock Text="{Binding QueriesProcessedToday}" />
    <TextBlock Text="{Binding AverageProcessingTime}" />
    <TextBlock Text="{Binding CurrentBatchProgress}" />
</StackPanel>
```

### Architectural Improvements

#### 13. Dependency Injection
**Proposal**: Use Microsoft.Extensions.DependencyInjection
```csharp
public class Startup
{
    public static IServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();
        
        services.AddSingleton<IHttpConnectionHandler, HttpConnectionHandler>();
        services.AddSingleton<IEZcadeConnection, EZcade_Connection_Handler>();
        services.AddTransient<MainWindow>();
        services.AddTransient<LoginWindow>();
        
        return services.BuildServiceProvider();
    }
}
```

#### 14. MVVM Pattern Implementation
**Current State**: Code-behind with mixed concerns  
**Proposal**: Separate ViewModels
```csharp
public class MainWindowViewModel : INotifyPropertyChanged
{
    private string _serialNumber;
    public string SerialNumber 
    { 
        get => _serialNumber;
        set 
        {
            _serialNumber = value;
            OnPropertyChanged(nameof(SerialNumber));
        }
    }
    
    public ICommand PrintCommand { get; }
    public ICommand EditCommand { get; }
    public ICommand CancelCommand { get; }
    
    // ... business logic moved from code-behind
}
```

#### 15. Unit Testing
**Proposal**: Add test coverage for critical paths
```csharp
[Fact]
public void SerialNumberQuery_ParsesPRQueryCorrectly()
{
    var query = new SerialNumberQuery("PR ABC123 DM 5");
    
    Assert.Equal("PR", query.Type);
    Assert.Equal("ABC123", query.ProductModel);
    Assert.Equal("DM", query.CodeType);
    Assert.Equal(5, query.Count);
    Assert.Null(query.PcbModel);
}

[Fact]
public void SerialNumberQuery_FormatsDMWithPrefix()
{
    var query = new SerialNumberQuery("PR MODEL DM 1");
    var formatted = query.GetSerialNumber("SERIAL123", "DM456");
    
    Assert.Equal("PRSERIAL123", formatted);
}
```

### Scalability Considerations

#### 16. Multi-Hardware Support
**Proposal**: Support multiple EZcade systems simultaneously
```csharp
public class MultiHardwareManager
{
    private Dictionary<string, EZcade_Connection_Handler> _connections = new();
    
    public void AddHardware(string identifier, string ip, int port)
    {
        var handler = new EZcade_Connection_Handler(ip, port);
        _connections[identifier] = handler;
    }
    
    public void ProcessRequest(string hardwareId, string query)
    {
        if (_connections.TryGetValue(hardwareId, out var handler))
        {
            // Route to specific hardware
        }
    }
}
```

#### 17. Server Failover
**Proposal**: Multiple server endpoints with automatic failover
```csharp
public class FailoverHttpHandler
{
    private List<string> _serverUrls = new()
    {
        "http://192.168.1.122:8080/",
        "http://192.168.1.123:8080/",  // Backup server
        "http://192.168.1.124:8080/"   // Tertiary server
    };
    
    public async Task<HttpResponseMessage> PostWithFailover(string endpoint, HttpContent content)
    {
        foreach (var baseUrl in _serverUrls)
        {
            try
            {
                _httpClient.BaseAddress = new Uri(baseUrl);
                var response = await _httpClient.PostAsync(endpoint, content);
                if (response.IsSuccessStatusCode)
                    return response;
            }
            catch
            {
                continue; // Try next server
            }
        }
        
        throw new Exception("All servers unavailable");
    }
}
```

---

## Summary

This Full Stack Design document provides a comprehensive overview of the EZcade_Client application, with emphasis on:

✓ **Core Features**: Detailed explanation of 7 major features including authentication, batch selection, serial generation, hardware communication, processing modes, data matrix handling, and activation workflows.

✓ **Logical Flows**: Step-by-step sequences for 5 critical workflows including startup, manual processing, automated processing, data transformation, and error handling.

✓ **State Management**: Complete UI state machine with transitions, conditions, and visual representations.

✓ **Configuration**: Comprehensive parameter documentation including hardcoded values and improvement opportunities.

✓ **Architecture**: Component diagrams, dependency graphs, and responsibility breakdowns.

✓ **Future Enhancements**: 17 categorized proposals ranging from high-priority security fixes to architectural improvements and scalability considerations.

This document serves as:
- **Developer Onboarding**: Complete understanding of system behavior
- **Maintenance Guide**: Reference for troubleshooting and debugging
- **Enhancement Roadmap**: Prioritized list of improvements
- **Technical Specification**: Detailed design for stakeholder review

For questions or clarifications, refer to the source code locations referenced throughout this document or contact the development team.

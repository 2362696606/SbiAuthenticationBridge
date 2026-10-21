/**
 * @file    ftmBridgeAPI.h
 * @brief   Top-level external interface header for FTM device communication SDK
 * @details Provides standardized cross-platform C interfaces for USB device open/close,
 *          device discovery, device information query, certificate loading, fingerprint image
 *          acquisition, custom data transmission & reception, and log configuration.
 *          Supports Windows and Linux systems.
 * @author  SDK Auto Generate
 * @date    2026
 * @version V1.0.0
 * @note    All interfaces are exported in standard C style, compatible with JNI / C# / C++ cross-language invocation
 */

#ifndef FTM_BRIDGEAPI_H
#define FTM_BRIDGEAPI_H

 /**
  * @brief Cross-platform dynamic library export macro definition
  * @note Uses import/export attribute on Windows; empty macro for Linux/Mac platforms
  */
#if defined(_WIN32) || defined(__CYGWIN__)
#ifdef FTM_EXPORTS
#define FTM_API __declspec(dllexport)
#else
#define FTM_API __declspec(dllimport)
#endif
#elif defined(__GNUC__) || defined(__clang__)
#define FTM_API __attribute__((visibility("default")))
#else
#define FTM_API
#endif

  /**
   * @brief C++ compatible extern "C" declaration
   * @note Prevents C++ name mangling to ensure cross-language call compatibility
   */
#ifdef __cplusplus
extern "C" {
#endif

	/**
	 * @typedef ICT_HANDLE
	 * @brief Global FTM device handle type
	 * @details Abstract USB device instance handle. All device operation interfaces depend on this handle.
	 * @note Non-NULL value indicates valid device; NULL means device closed or invalid handle
	 */
	typedef void* ICT_HANDLE;

	/**
	 * @brief  Open and initialize FTM USB device
	 * @return ICT_HANDLE Valid device handle
	 * @retval Non-NULL Device opened successfully
	 * @retval NULL Device open failed (detailed error logs will be printed)
	 * @note Automatically parses FingerPrintDebug.INI, matches device VID/PID, and applies USB endpoint resources
	 * @note Must be called first before any other device operation interfaces
	 */
	FTM_API ICT_HANDLE ftm_open();

	/**
	 * @brief  Android: open a USB device that the host application has already opened
	 * @param  fd [IN] Native file descriptor from UsbDeviceConnection.getFileDescriptor()
	 * @return ICT_HANDLE Valid device handle, or NULL on failure
	 * @note   Unprivileged Android apps cannot enumerate /dev/bus/usb. The host must
	 *         request USB permission through UsbManager, call UsbDeviceConnection.openDevice(),
	 *         claim the interface, then pass the descriptor here.
	 * @note   Call ftm_set_config_path() first if a non-default BULKOUT/BULKIN mapping is needed;
	 *         otherwise endpoints 0x01 (OUT) / 0x82 (IN) are used.
	 * @note   The caller keeps ownership of UsbDeviceConnection and must close it after ftm_close().
	 */
	FTM_API ICT_HANDLE ftm_open_fd(int fd);

	/**
	 * @brief  Set the INI file used to resolve VID/PID and bulk endpoints
	 * @param  path [IN] Absolute path to FingerPrintDebug.INI, or NULL to disable
	 * @note   Must be called before ftm_open()/ftm_open_fd(). Defaults to
	 *         "FingerPrintDebug.INI" relative to the current working directory.
	 *         On Android, pass the app-private absolute path.
	 */
	FTM_API void ftm_set_config_path(const char* path);

	/**
	 * @brief  Discover and probe online status and basic device capabilities
	 * @param  ictHandle   [IN] Valid device handle returned by ftm_open()
	 * @param  request_json [IN] Request JSON message buffer
	 * @param  wlen        [IN] Byte length of request JSON data
	 * @param  response    [OUT] Buffer for storing device discovery response JSON
	 * @param  timeout     [IN] Operation timeout in milliseconds, default 1000ms
	 * @return int Execution result code
	 * @retval >0 Success, returns valid response data length
	 * @retval <0 Failure, returns standard error code
	 * @note Communicates via custom frame protocol to detect device status and basic capabilities
	 */
	FTM_API int ftm_discover(ICT_HANDLE ictHandle, const char* request_json, int wlen, unsigned char* response, unsigned int timeout = 1000);

	/**
	 * @brief  Obtain detailed device information
	 * @param  ictHandle   [IN] Valid device handle
	 * @param  request_json [IN] Request parameter JSON buffer
	 * @param  wlen        [IN] Byte length of request JSON data
	 * @param  response    [OUT] Output buffer for device detailed information JSON
	 * @param  timeout     [IN] Operation timeout in milliseconds, default 1000ms
	 * @return int Execution result code
	 * @retval >0 Success, returns response data length
	 * @retval <0 Execution failure
	 * @note Supports reading firmware version, device ID, model, running status and other device attributes
	 */
	FTM_API int ftm_get_device_info(ICT_HANDLE ictHandle, const char* request_json, int wlen, unsigned char* response, unsigned int timeout = 1000);

	/**
	 * @brief  Load DER-format security certificate to device
	 * @param  ictHandle  [IN] Valid device handle
	 * @param  cert_der   [IN] Buffer storing DER binary certificate data
	 * @param  wlen       [IN] Byte length of DER certificate data
	 * @param  response   [OUT] Operation response data buffer
	 * @param  timeout    [IN] Operation timeout in milliseconds, default 1000ms
	 * @return int Execution result code
	 * @retval 0  Success
	 * @retval <0 Failure
	 * @note Pre-authentication interface for device identity verification and security authentication
	 */
	FTM_API int ftm_load_certificate(ICT_HANDLE ictHandle, const char* cert_der, int wlen, unsigned char* response, unsigned int timeout = 1000);

	/**
	 * @brief Fingerprint image acquisition for authentication scenario
	 * @param  ictHandle   [IN] Valid device handle
	 * @param  request_json [IN] 336-byte FCG-3M115 signed capture request buffer
	 * @param  wlen        [IN] Byte length of request data; must be 336
	 * @param  response    [OUT] Binary response buffer of at least 124104 bytes
	 * @param  timeout     [IN] Operation timeout in milliseconds, default 1000ms
	 * @return int Execution result code
	 * @retval 0 Success; response contains exactly 124104 bytes
	 * @retval <0 Acquisition failed
	 * @note The parameter name is retained for ABI compatibility; data is binary, not JSON
	 */
	FTM_API int ftm_capture_auth(ICT_HANDLE ictHandle, const char* request_json, int wlen, unsigned char* response, unsigned int timeout = 1000);

	/**
	 * @brief  Fingerprint image acquisition for user registration scenario
	 * @param  ictHandle   [IN] Valid device handle
	 * @param  request_json [IN] Acquisition configuration parameter JSON buffer
	 * @param  wlen        [IN] Byte length of configuration JSON data
	 * @param  response    [OUT] Output buffer for acquisition result and status
	 * @param  timeout     [IN] Operation timeout in milliseconds, default 1000ms
	 * @return int Execution result code
	 * @retval >0 Success, returns valid data length
	 * @retval <0 Acquisition failed
	 * @note Applied to user template enrollment and initial registration scenarios
	 */
	FTM_API int ftm_capture_reg(ICT_HANDLE ictHandle, const char* request_json, int wlen, unsigned char* response, unsigned int timeout = 1000);

	/**
	 * @brief  Get DKL public key from device
	 * @param  ictHandle   [IN] Valid device handle returned by ftm_open()
	 * @param  request_json [IN] Request parameter JSON buffer
	 * @param  wlen        [IN] Byte length of request JSON data
	 * @param  response    [OUT] Output buffer for DKL public key and status JSON
	 * @param  timeout     [IN] Operation timeout in milliseconds, default 1000ms
	 * @return int Execution result code
	 * @retval >0 Success, returns valid response data length
	 * @retval <0 Execution failure
	 * @note Used for device encryption/verification scenario, obtains device DKL public key for local signature verification and data encryption
	 */
	FTM_API int ftm_get_dkl_public_key(ICT_HANDLE ictHandle, const char* request_json, int wlen, unsigned char* response, unsigned int timeout = 1000);

	/**
	 * @brief  Set DKL certificate to device
	 * @param  ictHandle   [IN] Valid device handle returned by ftm_open()
	 * @param  request_json [IN] DKL certificate configuration parameter JSON buffer
	 * @param  wlen        [IN] Byte length of request JSON data
	 * @param  response    [OUT] Output buffer for configuration result JSON
	 * @param  timeout     [IN] Operation timeout in milliseconds, default 1000ms
	 * @return int Execution result code
	 * @retval >0 Success, returns valid response data length
	 * @retval <0 Execution failure
	 * @note Used for device DKL security authentication and certificate upgrade, takes effect immediately after configuration
	 */
	FTM_API int ftm_set_dkl_certificate(ICT_HANDLE ictHandle, const char* request_json, int wlen, unsigned char* response, unsigned int timeout = 1000);

	/**
	 * @brief  USB Bulk endpoint custom data transmit interface
	 * @param  ictHandle  [IN] Valid device handle
	 * @param  sendBuffer [IN] Buffer of raw binary data to be sent
	 * @param  wlen       [IN] Byte length of transmit data
	 * @param  timeout    [IN] Transmit timeout in milliseconds
	 * @return int Transmit result
	 * @retval >0 Actual transmitted byte count
	 * @retval <0 Standard libusb error code
	 * @note Low-level raw bulk out interface for custom protocol communication
	 */
	FTM_API int ftm_send(ICT_HANDLE ictHandle, const unsigned char* sendBuffer, int wlen, unsigned int timeout = 1000);

	/**
	 * @brief  USB Bulk endpoint custom data receive interface
	 * @param  ictHandle  [IN] Valid device handle
	 * @param  rcvBuffer  [OUT] Buffer for storing received data
	 * @param  rlen       [IN] Maximum receive buffer length
	 * @param  timeout    [IN] Receive timeout in milliseconds
	 * @return int Receive result
	 * @retval >0 Actual received byte count
	 * @retval <0 Standard error code
	 * @note Low-level raw bulk in reading interface
	 */
	FTM_API int ftm_recv(ICT_HANDLE ictHandle, unsigned char* rcvBuffer, int rlen, unsigned int timeout = 1000);

	/**
	 * @brief Close device and release all occupied resources
	 * @note Must be called in pair with ftm_open() before program exit
	 * @note Automatically releases USB interface, libusb context and device instance memory to avoid resource occupation and memory leakage
	 */
	FTM_API void ftm_close(ICT_HANDLE ictHandle);

	/**
	 * @brief  Initialize log module and configure output level
	 * @param  level [IN] Minimum log output level threshold
	 *          0 : FTM_LOG_LEVEL_DEBUG  Output all logs (Debug / Info / Warn / Error)
	 *          1 : FTM_LOG_LEVEL_INFO   Output Info / Warn / Error logs
	 *          2 : FTM_LOG_LEVEL_WARN   Only output Warn / Error logs
	 *          3 : FTM_LOG_LEVEL_ERROR  Only output Error logs
	 *          4 : FTM_LOG_LEVEL_NONE   Disable all log outputs
	 * @note Must be initialized first before ftm_open()
	 * @return None
	 */
	FTM_API void ftm_log_init(int level);

	/**
	 * @brief  Initialize the log module with an explicit output file path
	 * @param  path  [IN] Absolute log file path; NULL falls back to "sdk_runtime.log"
	 * @param  level [IN] Minimum log output level (see ftm_log_init)
	 * @note   On Android the current working directory is not writable; pass a path
	 *         under Context.FilesDir (for example filesDir + "/sdk_runtime.log").
	 */
	FTM_API void ftm_log_init_path(const char* path, int level);

	/**
	 * @brief  Synchronize system time to FTM device
	 * @param  ictHandle   [IN] Valid device handle
	 * @param  request_json [IN] Time synchronization parameter JSON buffer
	 * @param  wlen        [IN] Byte length of request JSON
	 * @param  timeout     [IN] Operation timeout in milliseconds, default 1000ms
	 * @return int Execution result code
	 */
	FTM_API int ftm_setTime(ICT_HANDLE ictHandle, const char* request_json, int wlen, unsigned int timeout = 1000);

	/**
	 * @brief  Set device working status to FTM device
	 * @param  ictHandle   [IN] Valid device handle
	 * @param  status      [IN] Device status enum value: MOSIP_NORMAL = 0 / MOSIP_BUSY = 1/ MOSIP_NOT_READY  = 2/ MOSIP_READY = 3/ MOSIP_NOT_REGISTERED = 4
	 * @param  timeout     [IN] Operation timeout in milliseconds, default 1000ms
	 * @return int Execution result code
	 */
	FTM_API int ftm_setDevStatus(ICT_HANDLE ictHandle, char status, unsigned int timeout = 1000);

	#define FTM_FW_STATE_IDLE       0
	#define FTM_FW_STATE_ERASING    1
	#define FTM_FW_STATE_RECEIVING  2
	#define FTM_FW_STATE_VERIFIED   3
	#define FTM_FW_STATE_PENDING    4

	typedef struct ftm_fw_status_t {
		unsigned char state;
		unsigned char reserved[3];
		unsigned int expected_len;
		unsigned int programmed_len;
		unsigned char version[8];
		unsigned char sha256[32];
	} ftm_fw_status_t;

	/**
	 * @brief Query slot1 upgrade state
	 * @retval 0 Success
	 * @retval <0 Failure
	 */
	FTM_API int ftm_fw_query(ICT_HANDLE ictHandle, ftm_fw_status_t *status, unsigned int timeout = 1000);

	/**
	 * @brief Erase slot1 and start receiving a signed MCUboot image
	 * @param image_len Byte length of the signed image, (0x200, 0xE0000]
	 * @param sha256 SHA-256 of the exact bytes that will be sent by ftm_fw_data
	 * @param version 8-byte image version from the MCUboot header, or zeros
	 * @note Device erase may take more than 10 seconds; use timeout >= 60000
	 */
	FTM_API int ftm_fw_begin(ICT_HANDLE ictHandle, unsigned int image_len, const unsigned char sha256[32],
	                         const unsigned char version[8], unsigned int timeout = 60000);

	/**
	 * @brief Write one slot1-relative chunk
	 * @param offset 4-byte aligned slot1 offset
	 * @param len Chunk length, 1..480
	 */
	FTM_API int ftm_fw_data(ICT_HANDLE ictHandle, unsigned int offset, const unsigned char *data,
	                        unsigned int len, unsigned int timeout = 1000);

	FTM_API int ftm_fw_finish(ICT_HANDLE ictHandle, unsigned int timeout = 5000);
	FTM_API int ftm_fw_apply(ICT_HANDLE ictHandle, unsigned int timeout = 3000);
	FTM_API int ftm_fw_abort(ICT_HANDLE ictHandle, unsigned int timeout = 1000);

#ifdef __cplusplus
}
#endif

#endif // FTM_BRIDGEAPI_H

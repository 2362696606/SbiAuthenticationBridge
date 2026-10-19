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
	 * @brief  Discover and probe online status and basic device capabilities
	 * @param  ictHandle   [IN] Valid device handle returned by ftm_open()
	 * @param  request_json [IN] Request JSON message buffer
	 * @param  wlen        [IN] Byte length of request JSON data
	 * @param  response    [OUT] Buffer for storing device discovery response JSON
	 * @param  timeout     [IN] Operation timeout in milliseconds, default 1000ms
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
	 * @retval 0 Success  Others failed
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
	 * @retval 0 Success  Others failed
	 * @note Pre-authentication interface for device identity verification and security authentication
	 */
	FTM_API int ftm_load_certificate(ICT_HANDLE ictHandle, const char* cert_der, int wlen, unsigned char* response, unsigned int timeout = 1000);

	/**
	 * @brief Fingerprint image acquisition for authentication scenario
	 * @param  ictHandle   [IN] Valid device handle
	 * @param  request_json [IN] Acquisition configuration parameter JSON buffer
	 * @param  wlen        [IN] Byte length of configuration JSON data
	 * @param  response    [OUT] Output buffer for acquisition result and status
	 * @param  timeout     [IN] Operation timeout in milliseconds, default 1000ms
	 * @return int Execution result code
	 * @retval 0 Success  Others failed
	 * @note Applied to identity comparison and verification authentication scenarios
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
	 * @retval 0 Success  Others failed
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
	 * @retval 0 Success  Others failed
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
	 * @retval 0 Success  Others failed
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
	 * @retval 0 Success  Others failed
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
	 * @retval 0 Success  Others failed
	 * @note Low-level raw bulk in reading interface
	 */
	FTM_API int ftm_recv(ICT_HANDLE ictHandle, unsigned char* rcvBuffer, int rlen, unsigned int timeout = 1000);

	/**
	 * @brief Close device and release all occupied resources
	 * @note Must be called in pair with ftm_open() before program exit
	 * @note Automatically releases USB interface, libusb context and device instance memory to avoid resource occupation and memory leakage
	 * @retval 0 Success  Others failed
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
	 * @brief  Synchronize system time to FTM device
	 * @param  ictHandle   [IN] Valid device handle
	 * @param  request_json [IN] Time synchronization parameter JSON buffer
	 * @param  wlen        [IN] Byte length of request JSON
	 * @param  timeout     [IN] Operation timeout in milliseconds, default 1000ms
	 * @return int Execution result code 0 Success  others failed
	 */
	FTM_API int ftm_setTime(ICT_HANDLE ictHandle, const char* request_json, int wlen, unsigned int timeout = 1000);

	/**
	 * @brief  Set device working status to FTM device
	 * @param  ictHandle   [IN] Valid device handle
	 * @param  status      [IN] Device status enum value: MOSIP_NORMAL = 0 / MOSIP_BUSY = 1/ MOSIP_NOT_READY  = 2/ MOSIP_READY = 3/ MOSIP_NOT_REGISTERED = 4
	 * @param  timeout     [IN] Operation timeout in milliseconds, default 1000ms
	 * @retval 0 Success  Others failed
	 */
	FTM_API int ftm_setDevStatus(ICT_HANDLE ictHandle, char status, unsigned int timeout = 1000);

	/**
	 * @brief  Trigger FTM device software reset
	 * @param  ictHandle   [IN] Valid device handle returned by ftm_open()
	 * @param  timeout     [IN] Operation timeout in milliseconds, default 1000ms
	 * @return int Execution result code
	 * @retval 0 Success  Others failed
	 * @note   Issues soft reset command to fingerprint sensor module;
	 * @note   will clear internal capture state, image buffer and working status.
	 * @note   After reset, device needs to be re-detected and reinitialized.
	 */
	FTM_API int ftm_device_reset(ICT_HANDLE ictHandle, unsigned int timeout = 1000);

#ifdef __cplusplus
}
#endif

#endif // FTM_BRIDGEAPI_H

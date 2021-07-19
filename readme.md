# Kentico license updating scheduled task

Task uses existing webservice on xperience.io website to update all existing licenses in an instance

## Description

Task purpose is bulk update of existing license keys within a Kentico Xperience administration instance. Its set up to run as a scheduled task within administration. 

It retrieves all of license keys within an instance and tries to use service on Xperience.io domain in order to update keys and store new ones. Task has set delay of 800ms per request to prevent hitting request limits on service side. 
It also provides retry policy with 3 attempts to make sure requests are not lost during network traffic. 

It sets its next run time automatically based on earliest expiry date detected in generated licenses. 

It can be hardcoded with desired credentials and it can also clean up old keys from an instance. 

If it is not prepared and hardcode takes in 3 parameters in Task data property of scheduled task (https://docs.xperience.io/configuring-xperience/scheduling-tasks/reference-scheduled-task-properties). Each should be on new line. 
1. User name used as a credential to access license service
2. License key serial used to verify and generate license keys agains Xperience license 
3. Number of license keys which should be generated in case you want only a certain number of keys generated 
4. Version number for which license key should be generated. If version is not set directly, key will be the same version as the serial number
5. true/false boolean check as to whether old license keys should be deleted

## Getting Started

### Dependencies

xperience.libraries 13.0.33
https://service.kentico.com/CMSLicenseService.asmx

### Installing

Standalone library set up with scheduled tasks. To configure task within the admin instance please follow documentation https://docs.xperience.io/configuring-xperience/scheduling-tasks/scheduling-custom-tasks

## Authors

Contributors names and contact info

Michal Samuhel 

## Version History

* 0.1
    * Initial Release

## License

This project is licensed under the MIT License


## Acknowledgments


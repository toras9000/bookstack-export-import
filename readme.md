# BookStack Import/Export script

This repository maintains scripts for batch copying BookStack books from one instance to another.  
It is made in a C# script that runs in script [dotnet-script](https://github.com/dotnet-script/dotnet-script).  

## Scripts

The script uses the BookStack API for import/export.  
The repository contains the following scripts  

- `bookstack-export-all.csx`
    - Export all books from the BookStack instance.
    - This is saved as a file on the file system.
- `bookstack-import.csx`
    - Import book data into a BookStack instance.
    - It reads the data stored by the export script and creates an equivalent entity in BookStack.

In both scripts, the target instance and the API key to be used are specified by rewriting the configuration variables in the script head.  
Export/Import makes a large number of API requests.  
BookStack has a limit on the number of API requests per minute, but the script waits for a certain amount of time when the limit is reached and automatically continues.  

### Cautions

These scripts only reproduce the contents of the book.  
The following points should be carefully identified.  

- Only books, chapters, pages, attachments, and gallery images can be exported/imported.
    - Shelves do not export/import.
    - Comments do not export/import.
- Ownership, permissions, etc. are restored on a name basis.
    - No users or roles are created.
    - Compare with already existing users and roles by name and restore only if uniquely identified.
    - Optionally, this restoration can be disabled.
    - If not restored, the owner is the API key user and permissions remain at default.
- The article will not be corrected in any way.
    - Even if the article contains the address of the export source instance, for example, it will not be updated to the import destination address.
    - If necessary, manual corrections can be made to the exported data before importing.
    - Note, however, that the slug of the imported entity may not be the same as the original slug.

## Script Execution

The following two installations are required to run C# scripts  

1. Install the .NET SDK.
    - Scripts are compiled for execution and require the SDK, not Runtime.
    - https://dotnet.microsoft.com/download
1. Install the dotnet-script.
    - .NET is already installed, you can install it by executing the following
      ```
      dotnet tool install -g dotnet-script
      ```

If the installation is successful, the following can be performed.  
```
dotnet script <target-script-file>
```

## bookstack-export-all.csx

As soon as the execution starts, the save process begins to execute, so it is necessary to rewrite the settings in the script in advance.  
When executed with the script tip variable set correctly, it saves all book data from the specified instance on the file system.  


## bookstack-import.csx

This script also requires rewriting the settings in the script beforehand.  
There are also a few options for import behavior, so read the comments in the script and set as needed.  
When executed, it asks where the exported data is located.  
When a location is entered, the data is read and the entity begins to be created in the import destination instance.  


## Test environment

The directory `test-services` contains files for the docker container environment for testing the scripts.  
However, explanations are omitted. If there is anything you do not understand after looking at it, you should not use it.  




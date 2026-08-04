Alpha Test 4

* A basic in game XML Editor, accessible via the main menu. Allows you to create new blocks, items, recipies, inside of the game. These edits, are saved to a special configuration file, which we then inject into the main game. Instead of modifying the raw config files of a mod or anything. At least, thats what I think should happen
 - Accessible via the main menu
 - Will parse the current XML and show you all the definitions currently
 - Can create new definitions, items, blocks,
 - These are then saved to a special file inside of lizzies mod which we then inject into the game once these things are loaded (items.xml... blocks.xml)
* Lay the foundations for the beginning of the download a mod feature. Theorise how we would allow users to download XML only based mods in game from a web service we will create which will turn into a mod portal. 
* Integrate the download a mod feature into many areas of the project, such as when we load a profile, if we can any uninstalled mods on our server at that correct version, offer to download them. Or, in the mod settings window, a button to find and install new mods in a new window pop up
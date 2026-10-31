import type { PluginDescriptor } from './rename-api';

/** Mirrors the built-in catalog served by `/api/rename-plugins` (`Application/Renaming/Plugins`). */
export const literalPlugin: PluginDescriptor = {
  typeId: 'literal',
  version: 1,
  name: 'Literal replacement',
  description: 'Replace a piece of text',
  defaultOptions: { search: '', replacement: '', ignoreCase: false, scope: 'all' },
  form: [
    {
      fields: [
        { key: 'search', label: 'Find text', kind: 'text' },
        { key: 'replacement', label: 'Replace with', kind: 'text' },
        {
          key: 'scope',
          label: 'Replace',
          kind: 'select',
          choices: [
            { value: 'all', label: 'Every match' },
            { value: 'first', label: 'First match only' },
          ],
        },
        { key: 'ignoreCase', label: 'Ignore case', kind: 'checkbox' },
      ],
    },
  ],
};

export const regexPlugin: PluginDescriptor = {
  typeId: 'regex',
  version: 1,
  name: 'Regular expression',
  description: 'Advanced pattern replace',
  defaultOptions: { pattern: '', replacement: '', ignoreCase: false },
  form: [
    {
      fields: [
        { key: 'pattern', label: 'Pattern', kind: 'text', hint: '.NET regular expression syntax' },
        {
          key: 'replacement',
          label: 'Replace with',
          kind: 'text',
          // biome-ignore lint/suspicious/noTemplateCurlyInString: ${name} is regex replacement syntax
          hint: 'Use $1 or ${name} for captured groups',
        },
        { key: 'ignoreCase', label: 'Ignore case', kind: 'checkbox' },
      ],
    },
  ],
};

export const casePlugin: PluginDescriptor = {
  typeId: 'case',
  version: 1,
  name: 'Case change',
  description: 'UPPER, lower or Title Case',
  defaultOptions: { mode: 'lower' },
  form: [
    {
      fields: [
        {
          key: 'mode',
          label: 'Case',
          kind: 'select',
          choices: [
            { value: 'upper', label: 'UPPERCASE' },
            { value: 'lower', label: 'lowercase' },
            { value: 'title', label: 'Title Case' },
          ],
        },
      ],
    },
  ],
};

export const affixPlugin: PluginDescriptor = {
  typeId: 'affix',
  version: 1,
  name: 'Prefix and suffix',
  description: 'Add text before or after the name',
  defaultOptions: { prefix: '', suffix: '' },
  form: [
    {
      fields: [
        { key: 'prefix', label: 'Prefix', kind: 'text', hint: 'Added before the name' },
        {
          key: 'suffix',
          label: 'Suffix',
          kind: 'text',
          hint: 'Added after the name, before the extension',
        },
      ],
    },
  ],
};

export const tmdbPlugin: PluginDescriptor = {
  typeId: 'tmdb',
  version: 1,
  name: 'TMDB metadata',
  description: 'Name movies and episodes from The Movie Database',
  singleInstance: true,
  defaultOptions: {
    preset: 'custom',
    nameTemplate: '',
    changeFolder: false,
    baseFolder: '',
    folderTemplate: '{title} ({year})',
    seasonFolder: false,
    seasonFolderTemplate: 'Season {season}',
    language: 'en-US',
  },
  form: [
    {
      heading: 'File name',
      fields: [
        {
          key: 'preset',
          label: 'Naming preset',
          kind: 'select',
          choices: [
            { value: 'custom', label: 'Custom' },
            { value: 'plexMovies', label: 'Plex — movies' },
            { value: 'plexTv', label: 'Plex — TV shows' },
            { value: 'jellyfinMovies', label: 'Jellyfin — movies' },
            { value: 'jellyfinTv', label: 'Jellyfin — TV shows' },
            { value: 'embyMovies', label: 'Emby — movies' },
            { value: 'embyTv', label: 'Emby — TV shows' },
            { value: 'kodiMovies', label: 'Kodi — movies' },
            { value: 'kodiTv', label: 'Kodi — TV shows' },
          ],
          hint: "Fills in the templates with a media server's recommended layout. Editing a template switches back to Custom.",
        },
        {
          key: 'nameTemplate',
          label: 'Filename template',
          kind: 'text',
          wide: true,
          placeholder: 'Leave empty to use the default',
          hint:
            'Fields: {title}, {originalTitle}, {year}, {showYear}, {airDate}, {tmdbId}, {season}, {seasonNumber}, {episode}, {episodeTitle}. ' +
            'Pad numbers with zeros, e.g. {episode:000}. Write {{ and }} for literal braces. ' +
            'Defaults: {title} ({year}) for movies, {title} - S{season}E{episode} - {episodeTitle} for episodes.',
        },
        {
          key: 'language',
          label: 'Metadata language',
          kind: 'text',
          hint: 'Sets the language of titles. Pick one or type your own.',
          suggestions: [
            'en-US',
            'en-GB',
            'es-ES',
            'ca-ES',
            'fr-FR',
            'de-DE',
            'it-IT',
            'pt-PT',
            'pt-BR',
            'ja-JP',
            'ko-KR',
            'zh-CN',
          ],
        },
      ],
    },
    {
      heading: 'Folders',
      fields: [{ key: 'changeFolder', label: 'Allow folder change', kind: 'checkbox' }],
    },
    {
      requires: ['changeFolder'],
      fields: [
        {
          key: 'baseFolder',
          label: 'Base folder',
          kind: 'text',
          placeholder: 'Library root',
          hint: 'Relative to the library root, with forward slashes, e.g. Movies/Archive.',
        },
        {
          key: 'folderTemplate',
          label: 'Folder template',
          kind: 'text',
          hint: 'Creates a folder inside the base folder, e.g. {title} ({year}) [tmdbid-{tmdbId}].',
        },
        { key: 'seasonFolder', label: 'Add season folder for episodes', kind: 'checkbox' },
      ],
    },
    {
      requires: ['changeFolder', 'seasonFolder'],
      fields: [
        {
          key: 'seasonFolderTemplate',
          label: 'Season folder template',
          kind: 'text',
          hint: 'Season {season} gives Season 01, Season {seasonNumber} gives Season 1, S{season} gives S01. Movies ignore it.',
        },
      ],
    },
  ],
};

export const builtInCatalog: PluginDescriptor[] = [
  literalPlugin,
  regexPlugin,
  casePlugin,
  tmdbPlugin,
];

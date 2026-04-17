update public.story_playlists
set show_on_home = true
where system_key in ('popular-stories-this-week', 'most-popular-stories')
  and show_on_home is distinct from true;
